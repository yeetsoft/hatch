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
/// An ambition's changes: the events that make one up, when it is worked
/// out, and what an epic says each did to it.
/// </summary>
public class IssueChangesControllerTests
{
    // ---- Reading ----

    [Fact]
    public async Task AFreshAmbition_ReadsNoChanges()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");

        Assert.Empty(await h.GetAsync(ambition));
    }

    [Fact]
    public async Task AChangeNotYetWorkedOut_ReadsOpen_WithNoWorkedOutAt()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");

        var change = Assert.Single(await h.GetAsync(ambition));

        Assert.Equal(added.EventId, change.Id);
        Assert.False(change.WorkedOut);
        Assert.Null(change.WorkedOutAt);
        Assert.Single(change.Entries);
    }

    [Fact]
    public async Task AChangeWorkedOut_ReadsWorkedOut_WithWhenItWas()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");
        var workedAt = Now.AddMinutes(5);

        await h.WorkedAsync(ambition, added.EventId, workedAt);

        var change = Assert.Single(await h.GetAsync(ambition));
        Assert.True(change.WorkedOut);
        Assert.Equal(workedAt, change.WorkedOutAt);
    }

    [Fact]
    public async Task TwoEditsSavedBeforeAWorkedRow_ReadAsOneChange()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");
        var edited = await h.EditCapabilityAsync(ambition, added.Id, "a edited", "a? edited");

        await h.WorkedAsync(ambition, edited.EventId, Now);

        var change = Assert.Single(await h.GetAsync(ambition));
        Assert.Equal(edited.EventId, change.Id);
        Assert.Equal(2, change.Entries.Count);
        Assert.Equal(added.EventId, change.Entries[0].EventId);
        Assert.Equal(edited.EventId, change.Entries[1].EventId);
    }

    [Fact]
    public async Task AReorderAlone_ReadsAsNoChange()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var first = await h.AddCapabilityAsync(ambition, "a", "a?");
        var second = await h.AddCapabilityAsync(ambition, "b", "b?");
        await h.WorkedAsync(ambition, second.EventId, Now);
        Assert.Single(await h.GetAsync(ambition));

        await h.Capabilities.PutOrder(ambition, new CapabilityOrderRequest([second.Id, first.Id]), default);

        Assert.Single(await h.GetAsync(ambition));
    }

    [Fact]
    public async Task AnEventWithNoHowToTell_ReadsTheLineAsNull()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var issue = await h.IssueAsync(ambition);

        h.Db.IssueEvents.Add(new EfHatchIssueEvent
        {
            IssueId = issue.Id, Actor = "test", Kind = EfHatchIssueEvent.CapabilityAdded,
            Payload = """{"id":1,"position":1,"statement":"s"}""", At = Now,
        });
        await h.Db.SaveChangesAsync();

        var change = Assert.Single(await h.GetAsync(ambition));
        var entry = Assert.Single(change.Entries);
        Assert.Equal("s", entry.StatementAfter);
        Assert.Null(entry.HowToTellAfter);
    }

    [Fact]
    public async Task AnOutcomeUnderAnOlderId_ShowsOnTheOpenChange()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var first = await h.AddCapabilityAsync(ambition, "a", "a?");
        await h.WorkedAsync(ambition, first.EventId, Now);

        var second = await h.AddCapabilityAsync(ambition, "b", "b?");
        await h.AddCapabilityAsync(ambition, "c", "c?");

        await h.RecordAsync(ambition, second.EventId, epic, EfHatchChangeOutcome.CarriesOn, "it still works");

        var open = (await h.GetAsync(ambition))[0];
        Assert.False(open.WorkedOut);
        var outcome = Assert.Single(open.Outcomes);
        Assert.Equal(epic, outcome.EpicKey);
        Assert.Equal(second.EventId, outcome.ChangeEventId);
    }

    [Fact]
    public async Task TheColumnOnAnOutcome_IsTheEpicsNow()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");
        await h.RecordAsync(ambition, added.EventId, epic, EfHatchChangeOutcome.CarriesOn, "why");

        await h.PutInAsync(epic, h.Review);

        var change = Assert.Single(await h.GetAsync(ambition));
        Assert.Equal("review", Assert.Single(change.Outcomes).EpicColumn);
    }

    // ---- GetChanges: not an ambition ----

    [Theory]
    [InlineData("epic")]
    [InlineData("story")]
    public async Task GetChanges_OnAnythingButAnAmbition_IsRefused(string type)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync(type);

        var result = await h.Controller.GetChanges(issue, default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"only an ambition has changes - {issue} is a {type}", refused.Value);
    }

    // ---- PostOutcome: the refusals, in order ----

    [Fact]
    public async Task PostOutcome_OnAnythingButAnAmbition_IsRefused()
    {
        var h = await NewAsync();
        var story = await h.FileAsync("story");

        var result = await h.Controller.PostOutcome(story, 1, new ChangeOutcomeRequest("AER-1", "carries_on", "why"), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"only an ambition has changes - {story} is a story", refused.Value);
    }

    [Fact]
    public async Task PostOutcome_WithAnEventIdThatIsNotAChangeEvent_IsRefused()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);

        var result = await h.Controller.PostOutcome(ambition, 999, new ChangeOutcomeRequest(epic, "carries_on", "why"), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"999 is not an added, edited or removed event of {ambition}", refused.Value);
    }

    [Fact]
    public async Task PostOutcome_WithAnUnparseableEpicKey_IsRefused()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");

        var result = await h.Controller.PostOutcome(ambition, added.EventId, new ChangeOutcomeRequest("nope", "carries_on", "why"), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("\"nope\" is not an issue key", refused.Value);
    }

    [Fact]
    public async Task PostOutcome_NamingNoIssue_IsRefused()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");

        var result = await h.Controller.PostOutcome(ambition, added.EventId, new ChangeOutcomeRequest("AER-999", "carries_on", "why"), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("there is no AER-999", refused.Value);
    }

    [Theory]
    [InlineData("story", null)]
    [InlineData("epic", "elsewhere")]
    public async Task PostOutcome_WithAnEpicThatIsNotADirectChild_IsRefused(string type, string? under)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");
        var parent = under == "elsewhere" ? await h.FileAsync("ambition") : ambition;
        var epic = await h.FileAsync(type, type == "epic" ? parent : null);

        var result = await h.Controller.PostOutcome(ambition, added.EventId, new ChangeOutcomeRequest(epic, "carries_on", "why"), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"{epic} is not an epic directly under {ambition}", refused.Value);
    }

    [Fact]
    public async Task PostOutcome_WithAWordThatIsNotAnOutcome_IsRefused()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");

        var result = await h.Controller.PostOutcome(ambition, added.EventId, new ChangeOutcomeRequest(epic, "fine", "why"), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("an outcome is one of carries_on, not_touched and serves_nothing", refused.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostOutcome_WithABlankWhy_IsRefused(string why)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");

        var result = await h.Controller.PostOutcome(ambition, added.EventId, new ChangeOutcomeRequest(epic, "carries_on", why), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("why may not be blank", refused.Value);
    }

    [Fact]
    public async Task PostOutcome_WithAWhyOverTheLimit_IsRefused()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");

        var result = await h.Controller.PostOutcome(
            ambition, added.EventId, new ChangeOutcomeRequest(epic, "carries_on", new string('x', EfHatchChangeOutcome.MaxWhyLength + 1)), default);

        var refused = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"why is at most {EfHatchChangeOutcome.MaxWhyLength} characters", refused.Value);
    }

    // ---- PostOutcome: writing ----

    [Fact]
    public async Task ARecord_WritesOneEventOnTheEpic()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");

        await h.RecordAsync(ambition, added.EventId, epic, EfHatchChangeOutcome.CarriesOn, "why");

        var evt = Assert.Single(await h.EpicEventsAsync(epic));
        Assert.Equal(EfHatchIssueEvent.ChangeOutcomeRecorded, evt.Kind);
        var payload = evt.Payload!.Value;
        Assert.Equal(ambition, payload.GetProperty("ambition").GetString());
        Assert.Equal(added.EventId, payload.GetProperty("changeEventId").GetInt64());
        Assert.Equal(EfHatchChangeOutcome.CarriesOn, payload.GetProperty("outcome").GetString());
        Assert.Equal("why", payload.GetProperty("why").GetString());
    }

    [Fact]
    public async Task ARecordForTheSameEpicInTheSameChange_Replaces_AndWritesASecondEvent()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");

        var first = await h.RecordAsync(ambition, added.EventId, epic, EfHatchChangeOutcome.CarriesOn, "it holds");
        var second = await h.RecordAsync(ambition, added.EventId, epic, EfHatchChangeOutcome.NotTouched, "never mind");

        Assert.Equal(EfHatchChangeOutcome.NotTouched, second.Outcome);
        Assert.Equal("never mind", second.Why);
        Assert.Equal(2, (await h.EpicEventsAsync(epic)).Count);

        var outcome = Assert.Single((await h.GetAsync(ambition))[0].Outcomes);
        Assert.Equal(EfHatchChangeOutcome.NotTouched, outcome.Outcome);
        _ = first;
    }

    [Fact]
    public async Task ARecordIdenticalToWhatIsThere_WritesNoAdditionalEvent()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");

        await h.RecordAsync(ambition, added.EventId, epic, EfHatchChangeOutcome.CarriesOn, "why");
        await h.RecordAsync(ambition, added.EventId, epic, EfHatchChangeOutcome.CarriesOn, "why");

        Assert.Single(await h.EpicEventsAsync(epic));
    }

    [Fact]
    public async Task ARowAlreadyDecided_IsRefused_AndNothingIsWritten()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");
        await h.RecordAsync(ambition, added.EventId, epic, EfHatchChangeOutcome.CarriesOn, "why");

        var row = await h.Db.ChangeOutcomes.SingleAsync();
        row.Decision = "kept";
        row.DecidedBy = "an operator";
        row.DecidedAt = Now;
        await h.Db.SaveChangesAsync();

        var result = await h.Controller.PostOutcome(
            ambition, added.EventId, new ChangeOutcomeRequest(epic, EfHatchChangeOutcome.NotTouched, "changed my mind"), default);

        var refused = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal($"a person has already decided {epic}'s outcome on this change", refused.Value);
        Assert.Single(await h.EpicEventsAsync(epic));
        Assert.Equal(EfHatchChangeOutcome.CarriesOn, (await h.Db.ChangeOutcomes.SingleAsync()).Outcome);
    }

    // ---- Passes: a row naming a change is left out ----

    [Fact]
    public async Task APassRowNamingAChange_IsLeftOutOfThePasses()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var issue = await h.IssueAsync(ambition);
        issue.StatusId = h.Doing;
        await h.Db.SaveChangesAsync();
        var added = await h.AddCapabilityAsync(ambition, "a", "a?");

        h.Db.WorkLog.Add(new EfHatchWorkLogEntry
        {
            IssueId = issue.Id, SessionId = "s1", StartedAt = Now, EndedAt = Now, DurationMs = 1,
            PlaybookFromStatusId = h.Doing, PlaybookToStatusId = h.Doing, IssueType = "ambition",
            Outcome = ClaimOutcomes.Worked, ChangeEventId = added.EventId, CreatedAt = Now,
        });
        h.Db.WorkLog.Add(new EfHatchWorkLogEntry
        {
            IssueId = issue.Id, SessionId = "s2", StartedAt = Now, EndedAt = Now.AddMinutes(1), DurationMs = 1,
            PlaybookFromStatusId = h.Doing, PlaybookToStatusId = h.Doing, IssueType = "ambition",
            Outcome = ClaimOutcomes.Worked, CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        var passes = Value(await h.Passes.GetPasses(ambition, default));

        Assert.Single(passes);
        Assert.Equal("s2", passes[0].SessionId);
    }

    // ---- Harness ----

    private sealed record Written(long Id, long EventId);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssueChangesController Controller { get; init; }
        public required IssueCapabilitiesController Capabilities { get; init; }
        public required IssuePassesController Passes { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required int ProjectId { get; init; }
        public required int Doing { get; init; }
        public required int Review { get; init; }

        public async Task<string> FileAsync(string type, string? parentKey = null)
        {
            var created = Created(await Issues.CreateIssue(
                new IssueCreateRequest(ProjectId, type, $"a {type}", null, parentKey, null, null), default));
            return created.Key;
        }

        public async Task<EfHatchIssue> IssueAsync(string key)
        {
            Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
            return await Db.Issues.WithKey(projectKey, number).SingleAsync();
        }

        public async Task PutInAsync(string key, int statusId)
        {
            var issue = await IssueAsync(key);
            issue.StatusId = statusId;
            await Db.SaveChangesAsync();
        }

        /// <summary>Adds a capability, and answers the id of the event it wrote.</summary>
        public async Task<Written> AddCapabilityAsync(string ambition, string statement, string howToTell)
        {
            var before = await EventIdsAsync(ambition);
            var added = Created(await Capabilities.PostCapability(ambition, new CapabilityCreateRequest(statement, howToTell), default));
            var eventId = (await EventIdsAsync(ambition)).Except(before).Single();
            return new Written(added.Id, eventId);
        }

        public async Task<Written> EditCapabilityAsync(string ambition, long id, string statement, string howToTell)
        {
            var before = await EventIdsAsync(ambition);
            await Capabilities.PatchCapability(ambition, id, new CapabilityPatchRequest(statement, howToTell), default);
            var eventId = (await EventIdsAsync(ambition)).Except(before).Single();
            return new Written(id, eventId);
        }

        private async Task<List<long>> EventIdsAsync(string ambition)
        {
            var issue = await IssueAsync(ambition);
            return await Db.IssueEvents.Where(e => e.IssueId == issue.Id).Select(e => e.Id).ToListAsync();
        }

        /// <summary>A work log row that worked out the change named by <paramref name="eventId"/>.</summary>
        public async Task WorkedAsync(string ambition, long eventId, DateTimeOffset endedAt)
        {
            var issue = await IssueAsync(ambition);
            Db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = issue.Id, SessionId = $"s-{eventId}", StartedAt = endedAt, EndedAt = endedAt, DurationMs = 1,
                Outcome = ClaimOutcomes.Worked, ChangeEventId = eventId, CreatedAt = endedAt,
            });
            await Db.SaveChangesAsync();
        }

        public async Task<ChangeDto[]> GetAsync(string ambition) => Value(await Controller.GetChanges(ambition, default));

        public async Task<ChangeOutcomeDto> RecordAsync(string ambition, long eventId, string epicKey, string outcome, string why)
        {
            var result = await Controller.PostOutcome(ambition, eventId, new ChangeOutcomeRequest(epicKey, outcome, why), default);
            return result.Result switch
            {
                CreatedAtActionResult created => (ChangeOutcomeDto)created.Value!,
                OkObjectResult ok => (ChangeOutcomeDto)ok.Value!,
                _ => result.Value ?? throw new InvalidOperationException($"expected an outcome, got {result.Result}"),
            };
        }

        public async Task<IReadOnlyList<IssueEventDto>> EpicEventsAsync(string epicKey) =>
            Value(await Thread.GetEvents(epicKey, default))
                .Where(e => e.Kind == EfHatchIssueEvent.ChangeOutcomeRecorded)
                .ToList();
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsImplementation = true };
        var review = new EfHatchStatus { Name = "review", SortOrder = 40 };
        db.AddRange(inbox, doing, review);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Controller = new IssueChangesController(db, caller, time),
            Capabilities = new IssueCapabilitiesController(db, caller, time),
            Passes = new IssuePassesController(db),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Thread = new IssueThreadController(db, caller, time),
            ProjectId = project.Id,
            Doing = doing.Id,
            Review = review.Id,
        };
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

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
        result.Value ?? throw new InvalidOperationException($"expected a value, got {result.Result}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {result.Result}");
}

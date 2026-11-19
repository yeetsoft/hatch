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
/// What an epic serves: the capabilities of the ambition it hangs beneath,
/// the descent rule that decides which those are, the one event a change
/// leaves, the reparent that would strand a link, and both reads.
/// </summary>
public class IssueServesControllerTests
{
    // ---- Writing ----

    [Fact]
    public async Task AnEpicUnderAnAmbition_ServesItsCapability()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var capability = await h.CapabilityAsync(ambition, "it works");
        var epic = await h.FileAsync("epic", parent: ambition);

        var served = await h.PutAsync(epic, capability.Id);

        var row = Assert.Single(served);
        Assert.Equal(ambition, row.AmbitionKey);
        Assert.Equal(capability.Id, row.CapabilityId);
        Assert.Equal(1, row.Position);
        Assert.Equal("it works", row.Statement);
        Assert.Equal("not_checked", row.State);
    }

    [Fact]
    public async Task AnEpicBeneathAnEpic_ServesTheAmbitionAboveBoth()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var capability = await h.CapabilityAsync(ambition, "it works");
        var outer = await h.FileAsync("epic", parent: ambition);
        var inner = await h.FileAsync("epic", parent: outer);

        Assert.Single(await h.PutAsync(inner, capability.Id));
    }

    [Fact]
    public async Task AnEpicBeneathAReflectionAndAnEpic_ServesTheAmbitionAboveThem()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var capability = await h.CapabilityAsync(ambition, "it works");
        var outer = await h.FileAsync("epic", parent: ambition);
        var reflection = await h.FileAsync("reflection", parent: outer);
        var inner = await h.FileAsync("epic", parent: reflection);

        Assert.Single(await h.PutAsync(inner, capability.Id));
    }

    [Fact]
    public async Task ACapabilityOfAnotherAmbition_IsRefused_NamingItAndItsOwner()
    {
        var h = await NewAsync();
        var mine = await h.FileAsync("ambition");
        var theirs = await h.FileAsync("ambition");
        var foreign = await h.CapabilityAsync(theirs, "not mine");
        var epic = await h.FileAsync("epic", parent: mine);

        var refusal = await h.RefusalAsync(epic, foreign.Id);

        Assert.Contains($"capability {foreign.Id}", refusal);
        Assert.Contains(theirs, refusal);
        Assert.Contains(mine, refusal);
        Assert.Empty(await h.ServedAsync(epic));
        Assert.Empty(await h.EventsAsync(epic));
    }

    [Fact]
    public async Task AnEpicBeneathNoAmbition_ServesNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var capability = await h.CapabilityAsync(ambition, "it works");
        var epic = await h.FileAsync("epic");

        var refusal = await h.RefusalAsync(epic, capability.Id);

        Assert.Contains($"capability {capability.Id}", refusal);
        Assert.Contains(ambition, refusal);
    }

    [Fact]
    public async Task ACapabilityThatDoesNotExist_IsRefused()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic");

        Assert.Contains("there is no capability 99", await h.RefusalAsync(epic, 99));
    }

    [Theory]
    [InlineData("story")]
    [InlineData("task")]
    [InlineData("ambition")]
    [InlineData("reflection")]
    public async Task AnIssueThatIsNotAnEpic_IsRefused(string type)
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var capability = await h.CapabilityAsync(ambition, "it works");
        var issue = await h.FileAsync(type);

        var refusal = await h.RefusalAsync(issue, capability.Id);

        Assert.Contains("only an epic serves capabilities", refusal);
        Assert.Contains($"is a {type}", refusal);
    }

    [Fact]
    public async Task AnUnknownKey_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Controller.PutServes("AER-99", new IssueServesWriteRequest([]), default)).Result);
        Assert.IsType<NotFoundResult>((await h.Controller.GetServes("AER-99", default)).Result);
    }

    [Fact]
    public async Task APutReplacesTheWholeSet()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var b = await h.CapabilityAsync(ambition, "b");
        var c = await h.CapabilityAsync(ambition, "c");
        var epic = await h.FileAsync("epic", parent: ambition);

        await h.PutAsync(epic, a.Id, b.Id);
        var served = await h.PutAsync(epic, b.Id, c.Id);

        Assert.Equal(new[] { b.Id, c.Id }, served.Select(s => s.CapabilityId));
        Assert.Equal(
            new[] { b.Id, c.Id },
            await h.Db.CapabilityLinks.Where(l => l.IssueId == h.IdOf(epic)).Select(l => l.CapabilityId).OrderBy(x => x).ToArrayAsync());
    }

    [Fact]
    public async Task AnEmptyPut_ClearsTheSet()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var epic = await h.FileAsync("epic", parent: ambition);
        await h.PutAsync(epic, a.Id);

        Assert.Empty(await h.PutAsync(epic));
    }

    [Fact]
    public async Task AKeyMayWrite_InAnyColumn()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var epic = await h.FileAsync("epic", parent: ambition);
        await h.PutInAsync(epic, h.Doing);
        h.ActAsKey();

        Assert.Single(await h.PutAsync(epic, a.Id));

        await h.PutInAsync(epic, h.Done);
        Assert.Empty(await h.PutAsync(epic));
    }

    // ---- The event ----

    [Fact]
    public async Task AChange_LeavesOneEvent_WithBothSidesAscending()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var b = await h.CapabilityAsync(ambition, "b");
        var c = await h.CapabilityAsync(ambition, "c");
        var epic = await h.FileAsync("epic", parent: ambition);

        await h.PutAsync(epic, c.Id, a.Id);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await h.PutAsync(epic, b.Id, c.Id);

        var events = await h.EventsAsync(epic);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("serves_changed", e.Kind));
        Assert.Equal("a person", events[1].Actor);

        var first = JsonDocument.Parse(events[0].Payload!.Value.GetRawText()).RootElement;
        Assert.Empty(first.GetProperty("from").EnumerateArray());
        Assert.Equal(new[] { a.Id, c.Id }, first.GetProperty("to").EnumerateArray().Select(e => e.GetInt64()));

        var second = JsonDocument.Parse(events[1].Payload!.Value.GetRawText()).RootElement;
        Assert.Equal(new[] { a.Id, c.Id }, second.GetProperty("from").EnumerateArray().Select(e => e.GetInt64()));
        Assert.Equal(new[] { b.Id, c.Id }, second.GetProperty("to").EnumerateArray().Select(e => e.GetInt64()));
    }

    [Fact]
    public async Task TheSetAlreadyThere_WritesNothing_InAnyOrder()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var b = await h.CapabilityAsync(ambition, "b");
        var epic = await h.FileAsync("epic", parent: ambition);
        await h.PutAsync(epic, a.Id, b.Id);
        var updatedAt = (await h.ReadAsync(epic)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));

        await h.PutAsync(epic, b.Id, a.Id, a.Id);

        Assert.Single(await h.EventsAsync(epic));
        Assert.Equal(updatedAt, (await h.ReadAsync(epic)).UpdatedAt);
    }

    // ---- Reading ----

    [Fact]
    public async Task TheRead_CarriesTheNewestVerdictAsState_AndReadsInAmbitionThenPositionOrder()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var b = await h.CapabilityAsync(ambition, "b");
        var epic = await h.FileAsync("epic", parent: ambition);
        await h.PutAsync(epic, b.Id, a.Id);
        await h.JudgeAsync(ambition, b.Id, "does_not_hold");
        await h.JudgeAsync(ambition, b.Id, "holds");

        var served = await h.ServedAsync(epic);

        Assert.Equal(new[] { "a", "b" }, served.Select(s => s.Statement));
        Assert.Equal(new[] { "not_checked", "holds" }, served.Select(s => s.State));
    }

    [Fact]
    public async Task TheRead_OnAnAmbitionAndAnUnrelatedIssue_IsEmpty()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var epic = await h.FileAsync("epic", parent: ambition);
        await h.PutAsync(epic, a.Id);
        var story = await h.FileAsync("story", parent: epic);

        Assert.Empty(await h.ServedAsync(ambition));
        Assert.Empty(await h.ServedAsync(story));
        Assert.Empty(await h.ServedAsync(await h.FileAsync("epic")));
    }

    [Fact]
    public async Task ACapability_ReadsTheKeysOfItsEpics_InKeyOrder()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var b = await h.CapabilityAsync(ambition, "b");
        var first = await h.FileAsync("epic", parent: ambition);
        var second = await h.FileAsync("epic", parent: ambition);
        await h.PutAsync(second, a.Id);
        await h.PutAsync(first, a.Id);

        var read = await h.CapabilitiesAsync(ambition);

        Assert.Equal(new[] { first, second }, read.Single(c => c.Id == a.Id).ServedBy);
        Assert.Empty(read.Single(c => c.Id == b.Id).ServedBy);
    }

    // ---- Reparenting ----

    [Fact]
    public async Task AReparentOutOfTheAmbition_IsRefused_NamingTheCapability()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var other = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "keeps the lights on");
        var epic = await h.FileAsync("epic", parent: ambition);
        var elsewhere = await h.FileAsync("epic", parent: other);
        await h.PutAsync(epic, a.Id);

        var refusal = await h.ReparentAsync(epic, elsewhere);

        Assert.Contains($"capability {a.Id}", refusal);
        Assert.Contains("keeps the lights on", refusal);
        Assert.Contains(ambition, refusal);
        Assert.Equal(ambition, (await h.ReadAsync(epic)).ParentKey);
    }

    [Fact]
    public async Task AReparentToNoParent_IsRefused()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var epic = await h.FileAsync("epic", parent: ambition);
        await h.PutAsync(epic, a.Id);

        Assert.Contains($"capability {a.Id}", await h.ReparentAsync(epic, ""));
    }

    [Fact]
    public async Task AReparentWithinTheSameAmbition_IsUnaffected()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var epic = await h.FileAsync("epic", parent: ambition);
        var sibling = await h.FileAsync("epic", parent: ambition);
        await h.PutAsync(epic, a.Id);

        Assert.Null(await h.ReparentAsync(epic, sibling));
        Assert.Equal(sibling, (await h.ReadAsync(epic)).ParentKey);
        Assert.Single(await h.ServedAsync(epic));
    }

    [Fact]
    public async Task AReparentOfAnEpicThatServesNothing_IsUnaffected()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", parent: ambition);
        var elsewhere = await h.FileAsync("epic");

        Assert.Null(await h.ReparentAsync(epic, elsewhere));
    }

    [Fact]
    public async Task AReparentOfAnEpicAboveOneThatServes_IsRefused_NamingBoth()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var outer = await h.FileAsync("epic", parent: ambition);
        var inner = await h.FileAsync("epic", parent: outer);
        var elsewhere = await h.FileAsync("epic");
        await h.PutAsync(inner, a.Id);

        var refusal = await h.ReparentAsync(outer, elsewhere);

        Assert.Contains(inner, refusal);
        Assert.Contains($"capability {a.Id}", refusal);
    }

    // ---- Deleting ----

    [Fact]
    public async Task DeletingTheEpic_LeavesTheCapability()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var a = await h.CapabilityAsync(ambition, "a");
        var epic = await h.FileAsync("epic", parent: ambition);
        await h.PutAsync(epic, a.Id);

        // The in-memory provider does not cascade, so the link goes by hand and
        // the cascade itself is pinned as metadata in HatchContextTests.
        h.Db.CapabilityLinks.RemoveRange(h.Db.CapabilityLinks);
        h.Db.Issues.Remove(await h.Db.Issues.SingleAsync(i => i.Id == h.IdOf(epic)));
        await h.Db.SaveChangesAsync();

        var read = Assert.Single(await h.CapabilitiesAsync(ambition));
        Assert.Empty(read.ServedBy);
        Assert.Empty(await h.Db.CapabilityLinks.ToListAsync());
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required IssueServesController Controller { get; init; }
        public required IssueCapabilitiesController Capabilities { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required int Doing { get; init; }
        public required int Done { get; init; }

        public async Task<string> FileAsync(string type, string? parent = null)
        {
            var result = await Issues.CreateIssue(
                new IssueCreateRequest(ProjectId, type, $"a {type}", null, parent, null, null), default);
            return result.Result is CreatedAtActionResult created
                ? ((IssueDto)created.Value!).Key
                : result.Value?.Key ?? throw new InvalidOperationException($"could not file {type}: {result.Result}");
        }

        public long IdOf(string key)
        {
            Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
            return Db.Issues.WithKey(projectKey, number).Select(i => i.Id).Single();
        }

        public async Task PutInAsync(string key, int statusId)
        {
            var id = IdOf(key);
            var issue = await Db.Issues.SingleAsync(i => i.Id == id);
            issue.StatusId = statusId;
            await Db.SaveChangesAsync();
        }

        public void ActAsKey()
        {
            Caller.Person = null;
            Caller.Key = new EfApiKey { Name = "hatch", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };
        }

        public async Task<CapabilityDto> CapabilityAsync(string ambition, string statement)
        {
            var result = await Capabilities.PostCapability(ambition, new CapabilityCreateRequest(statement, "look"), default);
            return result.Result is CreatedAtActionResult created
                ? (CapabilityDto)created.Value!
                : throw new InvalidOperationException($"expected a created capability, got {result.Result}");
        }

        public async Task<CapabilityDto[]> CapabilitiesAsync(string ambition) =>
            Value(await Capabilities.GetCapabilities(ambition, default));

        public async Task JudgeAsync(string ambition, long id, string verdict) =>
            Assert.IsType<CreatedAtActionResult>(
                (await Capabilities.PostVerdict(ambition, id, new CapabilityVerdictRequest(verdict, "the code", "abc1234"), default)).Result);

        public async Task<ServedCapabilityDto[]> PutAsync(string key, params long[] ids) =>
            Value(await Controller.PutServes(key, new IssueServesWriteRequest(ids), default));

        public async Task<string> RefusalAsync(string key, params long[] ids)
        {
            var result = await Controller.PutServes(key, new IssueServesWriteRequest(ids), default);
            return Assert.IsType<BadRequestObjectResult>(result.Result).Value!.ToString()!;
        }

        public async Task<ServedCapabilityDto[]> ServedAsync(string key) => Value(await Controller.GetServes(key, default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The refusal a reparent got, or null when it went through.</summary>
        public async Task<string?> ReparentAsync(string key, string parent)
        {
            var result = await Issues.PatchIssue(
                key, new IssuePatchRequest(null, null, null, null, parent, null, null, null, null, null), default);
            return result.Result is ObjectResult { Value: string refusal } ? refusal : null;
        }

        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == "serves_changed")
                .Reverse()
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
        var done = new EfHatchStatus { Name = "done", SortOrder = 90, IsTerminal = true };
        db.AddRange(inbox, doing, done);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "a person", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Controller = new IssueServesController(db, caller, time),
            Capabilities = new IssueCapabilitiesController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = project.Id,
            Doing = doing.Id,
            Done = done.Id,
        };
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Whoever the test says is holding the phone. Their name is the audit actor.</summary>
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
}

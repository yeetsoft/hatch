using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// An ambition's dollar budget: what it accepts, what it refuses, what it writes
/// in the trail, how spent is added up - and that an API key can read it and can
/// write it by no route at all.
/// </summary>
public class IssueBudgetControllerTests
{
    // ---- Setting, changing, clearing ----

    [Fact]
    public async Task ABudget_IsSetChangedAndReadBack()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");

        var set = await h.SetAsync(ambition, "50");
        Assert.Equal(50m, set.BudgetUsd);
        Assert.Equal(50m, (await h.ReadAsync(ambition)).BudgetUsd);

        var changed = await h.SetAsync(ambition, "75.25");
        Assert.Equal(75.25m, changed.BudgetUsd);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnEmptyString_OrWhitespace_ClearsIt(string empty)
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");
        await h.SetAsync(ambition, "50");

        var cleared = await h.SetAsync(ambition, empty);

        Assert.Null(cleared.BudgetUsd);
        Assert.Null((await h.ReadAsync(ambition)).BudgetUsd);
    }

    [Fact]
    public async Task ANullAmount_ChangesNothing()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");
        await h.SetAsync(ambition, "50");

        var left = Value(await h.Budget.PatchIssueBudget(ambition, new IssueBudgetRequest(null), default));

        Assert.Equal(50m, left.BudgetUsd);
        Assert.Single(await h.EventsAsync(ambition));
    }

    [Fact]
    public async Task TheEvent_HoldsFromToAndWho()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");

        await h.SetAsync(ambition, "50");
        await h.SetAsync(ambition, "80");
        await h.SetAsync(ambition, "");

        var events = await h.EventsAsync(ambition);
        Assert.Equal(3, events.Count);
        Assert.All(events, e => Assert.Equal("Nathan", e.Actor));
        Assert.Equal((null, 50m), Sides(events[0]));
        Assert.Equal((50m, 80m), Sides(events[1]));
        Assert.Equal((80m, null), Sides(events[2]));
    }

    [Fact]
    public async Task SettingTheSameBudget_OrClearingNone_WritesNoEvent()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");

        await h.SetAsync(ambition, "");
        await h.SetAsync(ambition, "50");
        await h.SetAsync(ambition, "50.00");

        Assert.Single(await h.EventsAsync(ambition));
    }

    // ---- Refusals ----

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("12.345")]
    [InlineData("$5")]
    [InlineData("1e3")]
    [InlineData("1000000000.01")]
    public async Task ABadAmount_IsRefusedWithASentence_AndNothingIsWritten(string amount)
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");
        await h.SetAsync(ambition, "50");

        var refused = Assert.IsType<BadRequestObjectResult>(
            (await h.Budget.PatchIssueBudget(ambition, new IssueBudgetRequest(amount), default)).Result);

        Assert.Equal($"a budget is a positive number of dollars, to the cent - not \"{amount}\"", refused.Value);
        Assert.Equal(50m, (await h.ReadAsync(ambition)).BudgetUsd);
        Assert.Single(await h.EventsAsync(ambition));
    }

    [Fact]
    public async Task TheCap_IsAccepted()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");

        Assert.Equal(1_000_000_000m, (await h.SetAsync(ambition, "1000000000")).BudgetUsd);
    }

    [Theory]
    [InlineData("story")]
    [InlineData("task")]
    [InlineData("bug")]
    [InlineData("epic")]
    [InlineData("reflection")]
    public async Task AnythingButAnAmbition_IsRefusedNamingItsType_OnWriteAndRead(string type)
    {
        var h = await NewAsync();
        var issue = await h.SeedAsync(type);
        var expected = $"only an ambition takes a budget - {issue} is a {type}";

        var write = Assert.IsType<BadRequestObjectResult>(
            (await h.Budget.PatchIssueBudget(issue, new IssueBudgetRequest("50"), default)).Result);
        var read = Assert.IsType<BadRequestObjectResult>((await h.Budget.GetIssueBudget(issue, default)).Result);

        Assert.Equal(expected, write.Value);
        Assert.Equal(expected, read.Value);
        Assert.Null((await h.Db.Issues.SingleAsync(i => i.Type == type)).BudgetUsd);
    }

    [Fact]
    public async Task AnUnknownKey_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Budget.GetIssueBudget("AER-99", default)).Result);
        Assert.IsType<NotFoundResult>(
            (await h.Budget.PatchIssueBudget("AER-99", new IssueBudgetRequest("5"), default)).Result);
    }

    // ---- The one edge that is cut ----

    [Fact]
    public void SettingIt_IsClosedToAnApiKey_AndReadingItIsOpen()
    {
        static RequireRoleAttribute Guard(string method) =>
            typeof(IssueBudgetController).GetMethod(method)!
                .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
                .Cast<RequireRoleAttribute>()
                .Single();

        Assert.False(Guard(nameof(IssueBudgetController.PatchIssueBudget)).AcceptKey);
        Assert.True(Guard(nameof(IssueBudgetController.GetIssueBudget)).AcceptKey);
        Assert.Empty(typeof(IssueBudgetController).GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    [Fact]
    public async Task AKey_IsRefusedWithASentence_AndWritesNothing()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");

        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var refused = Assert.IsType<ObjectResult>(
            (await h.Budget.PatchIssueBudget(ambition, new IssueBudgetRequest("50"), default)).Result);

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusCode);
        Assert.Equal("an ambition's budget is a person's to set - not an agent's", refused.Value);
        Assert.Null((await h.Db.Issues.SingleAsync()).BudgetUsd);
        Assert.Empty(await h.EventsAsync(ambition));
    }

    [Fact]
    public async Task AKey_CanReadIt()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");
        await h.SetAsync(ambition, "50");

        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a key", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        Assert.Equal(50m, Value(await h.Budget.GetIssueBudget(ambition, default)).BudgetUsd);
    }

    [Fact]
    public async Task NoOtherRoute_TakesABudget()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");
        await h.SetAsync(ambition, "50");
        var before = (await h.EventsAsync(ambition)).Count;

        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = """{"budget":"90","budgetUsd":90}""";

        var patch = JsonSerializer.Deserialize<IssuePatchRequest>(json, web)!;
        Value(await h.Issues.PatchIssue(ambition, patch, default));

        var bulk = JsonSerializer.Deserialize<IssueBulkEditRequest>(
            """{"keys":["%KEY%"],"budget":"90","budgetUsd":90}""".Replace("%KEY%", ambition), web)!;
        await h.Issues.BulkEdit(bulk, default);

        Assert.Equal(50m, (await h.ReadAsync(ambition)).BudgetUsd);
        Assert.Equal(before, (await h.EventsAsync(ambition)).Count);
    }

    // ---- Spent ----

    [Fact]
    public async Task Spent_IsEveryRowOnTheAmbitionAndBeneathIt_InAnyProject()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");
        var epic = await h.SeedAsync("epic", parent: ambition);
        var story = await h.SeedAsync("story", parent: epic);
        var task = await h.SeedAsync("task", parent: story);
        var other = await h.SeedAsync("epic", parent: ambition, project: h.OtherProjectId);
        var stranger = await h.SeedAsync("epic");

        await h.LogAsync(ambition, 1.00m);
        await h.LogAsync(epic, 2.00m);
        await h.LogAsync(story, 3.00m);
        await h.LogAsync(task, 4.00m);
        await h.LogAsync(other, 5.00m);
        await h.LogAsync(stranger, 100m);
        await h.LogAsync(task, null);
        await h.LogAsync(story, null);

        var read = await h.ReadBudgetAsync(ambition);

        Assert.Equal(15.00m, read.SpentUsd);
        Assert.Equal(2, read.UnpricedSessions);
        Assert.Null(read.BudgetUsd);
    }

    [Fact]
    public async Task AnAmbitionWithNoBudget_IsNotSpent_WhateverItHasCost()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");
        await h.LogAsync(ambition, 5_000m);

        var read = await h.ReadBudgetAsync(ambition);

        Assert.Null(read.BudgetUsd);
        Assert.Equal(5_000m, read.SpentUsd);
        Assert.False(read.Spent);
    }

    [Fact]
    public async Task ItIsSpent_WhenTheSumReachesTheBudget_AndNotBefore()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");
        await h.SetAsync(ambition, "10");

        await h.LogAsync(ambition, 9.99m);
        Assert.False((await h.ReadBudgetAsync(ambition)).Spent);

        await h.LogAsync(ambition, 0.01m);
        var read = await h.ReadBudgetAsync(ambition);
        Assert.Equal(10m, read.SpentUsd);
        Assert.True(read.Spent);
    }

    [Fact]
    public async Task Spent_IsWorkedOutOnEachRead()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");
        var epic = await h.SeedAsync("epic", parent: ambition);
        await h.LogAsync(epic, 1.50m);

        var first = await h.ReadBudgetAsync(ambition);
        await h.LogAsync(epic, 2.25m);
        var second = await h.ReadBudgetAsync(ambition);

        Assert.Equal(1.50m, first.SpentUsd);
        Assert.Equal(3.75m, second.SpentUsd);
    }

    [Fact]
    public async Task Spent_DoesNotDependOnWhoReads_AScopeThatHidesAProjectStillGetsTheWholeFigure()
    {
        var h = await NewAsync();
        var ambition = await h.SeedAsync("ambition");
        var visible = await h.SeedAsync("epic", parent: ambition);
        var hidden = await h.SeedAsync("epic", parent: ambition, project: h.OtherProjectId);
        await h.LogAsync(ambition, 1m);
        await h.LogAsync(visible, 2m);
        await h.LogAsync(hidden, 4m);

        await using var scoped = ScopeSeed.Request(h.Options, ScopeSeed.Only(h.ProjectId));
        var issue = await scoped.Issues.SingleAsync(i => i.Type == "ambition");

        // The scope really does hide the second project's epic and its work.
        Assert.Equal(2, await scoped.Issues.CountAsync());
        Assert.Equal(3m, (await WorkLogRollup.TotalsAsync(scoped, issue.Id, includeDescendants: true, default)).CostUsd);

        // ...and the budget's own sum does not notice.
        var read = await AmbitionBudget.ReadAsync(scoped, issue, default);
        Assert.Equal(7m, read.SpentUsd);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        private int _next;

        public required DbContextOptions<HatchContext> Options { get; init; }
        public required HatchContext Db { get; init; }
        public required IssueBudgetController Budget { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required int ProjectId { get; init; }
        public required int OtherProjectId { get; init; }
        public required int StatusId { get; init; }

        /// <summary>Files an issue straight into the store and returns its key.</summary>
        public async Task<string> SeedAsync(string type, string? parent = null, int? project = null)
        {
            var projectId = project ?? ProjectId;
            var number = ++_next;
            var parentId = parent is null ? (long?)null : (await ByKeyAsync(parent)).Id;

            var issue = new EfHatchIssue
            {
                ProjectId = projectId, Number = number, Type = type, Title = $"a {type}", StatusId = StatusId,
                ParentId = parentId, Rank = number * 1000, CreatedBy = "test", CreatedAt = Now, UpdatedAt = Now,
            };
            Db.Add(issue);
            await Db.SaveChangesAsync();

            var key = (await Db.Projects.SingleAsync(p => p.Id == projectId)).Key;
            return IssueKey.Format(key, number);
        }

        public async Task LogAsync(string key, decimal? cost)
        {
            Db.WorkLog.Add(new EfHatchWorkLogEntry
            {
                IssueId = (await ByKeyAsync(key)).Id, SessionId = $"s-{Guid.NewGuid():N}",
                StartedAt = Now, EndedAt = Now, DurationMs = 0, CreatedAt = Now, CostUsd = cost,
            });
            await Db.SaveChangesAsync();
        }

        public Task<IssueBudgetDto> SetAsync(string key, string amount) =>
            ValueAsync(Budget.PatchIssueBudget(key, new IssueBudgetRequest(amount), default));

        public Task<IssueBudgetDto> ReadAsync(string key) => ReadBudgetAsync(key);

        public Task<IssueBudgetDto> ReadBudgetAsync(string key) => ValueAsync(Budget.GetIssueBudget(key, default));

        /// <summary>The budget's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.BudgetChanged)
                .Reverse()
                .ToList();

        private async Task<EfHatchIssue> ByKeyAsync(string key)
        {
            Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
            return await Db.Issues.WithKey(projectKey, number).SingleAsync();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var options = new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new HatchContext(options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        var other = new EfHatchProject { Key = "OTH", Name = "Other", CreatedAt = Now };
        var status = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        db.AddRange(hatch, other, status);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Options = options,
            Db = db,
            Budget = new IssueBudgetController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Thread = new IssueThreadController(db, caller, time),
            Caller = caller,
            ProjectId = hatch.Id,
            OtherProjectId = other.Id,
            StatusId = status.Id,
        };
    }

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

    /// <summary>The <c>from</c> and <c>to</c> a <c>budget_changed</c> event carries.</summary>
    private static (decimal? From, decimal? To) Sides(IssueEventDto e)
    {
        var payload = e.Payload!.Value;
        return (
            payload.GetProperty("from").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("from").GetDecimal(),
            payload.GetProperty("to").ValueKind == JsonValueKind.Null ? null : payload.GetProperty("to").GetDecimal());
    }

    private static async Task<T> ValueAsync<T>(Task<ActionResult<T>> call) => Value(await call);

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    /// <summary>The plain-text reason on a refusal - what the UI puts on screen.</summary>
    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

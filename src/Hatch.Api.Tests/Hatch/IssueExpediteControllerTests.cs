using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <em>This one first, or first of all.</em> What the route writes, what it
/// refuses to write twice, what nothing else is allowed to clear, the legacy
/// two-level alias, and the property the whole design rests on, which is that
/// an API key cannot set either route.
/// </summary>
public class IssueExpediteControllerTests
{
    // ---- Marking, and unmarking ----

    [Fact]
    public async Task AnIssue_IsNormalUntilSomebodySaysSo()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.False(issue.Expedited);
        Assert.Equal(PriorityLevels.NormalName, issue.Priority);
    }

    [Fact]
    public async Task AnIssue_IsMarkedExpedited()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var marked = await h.PriorityAsync(issue.Key, PriorityLevels.ExpeditedName);

        Assert.True(marked.Expedited);
        Assert.Equal(PriorityLevels.ExpeditedName, marked.Priority);
        Assert.Equal(PriorityLevels.Expedited, (await h.RowAsync(issue.Key)).Priority);
    }

    [Fact]
    public async Task AnIssue_IsMarkedEmergency()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var marked = await h.PriorityAsync(issue.Key, PriorityLevels.EmergencyName);

        Assert.True(marked.Expedited);
        Assert.Equal(PriorityLevels.EmergencyName, marked.Priority);
        Assert.Equal(PriorityLevels.Emergency, (await h.RowAsync(issue.Key)).Priority);
    }

    [Fact]
    public async Task AnIssue_IsMarkedEconomy()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var marked = await h.PriorityAsync(issue.Key, PriorityLevels.EconomyName);

        Assert.False(marked.Expedited);
        Assert.Equal(PriorityLevels.EconomyName, marked.Priority);
        Assert.Equal(PriorityLevels.Economy, (await h.RowAsync(issue.Key)).Priority);
    }

    [Fact]
    public async Task TheSameRoute_UnmarksIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PriorityAsync(issue.Key, PriorityLevels.EmergencyName);
        var unmarked = await h.PriorityAsync(issue.Key, PriorityLevels.NormalName);

        Assert.False(unmarked.Expedited);
        Assert.Equal(PriorityLevels.NormalName, unmarked.Priority);
        Assert.Equal(PriorityLevels.Normal, (await h.RowAsync(issue.Key)).Priority);
    }

    [Fact]
    public async Task AnUnknownLevel_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var result = await h.Priority.PutIssuePriority(issue.Key, new PriorityRequest("urgent"), default);

        Assert.Contains("not a priority", Reason(result.Result));
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.Priority.PutIssuePriority("AER-404", new PriorityRequest(PriorityLevels.ExpeditedName), default)).Result);
        Assert.IsType<NotFoundResult>(
            (await h.Expedite.PutIssueExpedite("AER-404", new ExpediteRequest(true), default)).Result);
    }

    // ---- The trail ----

    [Fact]
    public async Task Marking_WritesWhoDidItAndWhichWayByName()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PriorityAsync(issue.Key, PriorityLevels.EmergencyName);

        var e = Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal("Nathan", e.Actor);
        Assert.Equal((PriorityLevels.NormalName, PriorityLevels.EmergencyName), Sides(e));
    }

    [Fact]
    public async Task Unmarking_WritesItsOwnRowTheOtherWayRound()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PriorityAsync(issue.Key, PriorityLevels.ExpeditedName);
        await h.PriorityAsync(issue.Key, PriorityLevels.NormalName);

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.Equal((PriorityLevels.NormalName, PriorityLevels.ExpeditedName), Sides(events[0]));
        Assert.Equal((PriorityLevels.ExpeditedName, PriorityLevels.NormalName), Sides(events[1]));
    }

    // ---- Setting what it already holds ----

    [Fact]
    public async Task SettingTheValueItAlreadyHolds_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PriorityAsync(issue.Key, PriorityLevels.ExpeditedName);

        var again = await h.PriorityAsync(issue.Key, PriorityLevels.ExpeditedName);

        Assert.Equal(PriorityLevels.ExpeditedName, again.Priority);
        Assert.Single(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task SettingTheValueItAlreadyHolds_DoesNotMoveTheUpdatedTime()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PriorityAsync(issue.Key, PriorityLevels.ExpeditedName);

        var was = (await h.ReadAsync(issue.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.PriorityAsync(issue.Key, PriorityLevels.ExpeditedName);

        Assert.Equal(was, (await h.ReadAsync(issue.Key)).UpdatedAt);
    }

    [Fact]
    public async Task UnmarkingSomethingNeverMarked_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var unmarked = await h.PriorityAsync(issue.Key, PriorityLevels.NormalName);

        Assert.Equal(PriorityLevels.NormalName, unmarked.Priority);
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    // ---- Nothing else clears it ----

    [Fact]
    public async Task Moving_RetitlingAndReparenting_LeaveItAsItWasSet()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var issue = await h.FileAsync("story", "the story");
        await h.PriorityAsync(issue.Key, PriorityLevels.EmergencyName);

        // Every other way an issue is written, one after another. The level is
        // written by one route and read by everything, so "nothing clears it"
        // is a claim about every other route rather than about this one.
        await h.PatchAsync(issue.Key, new IssuePatchRequest(
            Title: "renamed", Description: null, Type: null, StatusId: null, ParentKey: epic.Key,
            ReadyAt: null, DueAt: null, PullRequestUrl: null));

        var moved = Value(await h.Issues.MoveIssue(
            issue.Key, new IssueMoveRequest(h.DoneId, null, null), default));

        Assert.Equal(PriorityLevels.EmergencyName, moved.Priority);
        Assert.Equal(PriorityLevels.Emergency, (await h.RowAsync(issue.Key)).Priority);
    }

    // ---- The edge that is cut ----

    [Fact]
    public void SettingIt_IsClosedToAnApiKey()
    {
        AssertRefusesAKey(nameof(IssueExpediteController.PutIssuePriority));
        AssertRefusesAKey(nameof(IssueExpediteController.PutIssueExpedite));

        Assert.Empty(typeof(IssueExpediteController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    private static void AssertRefusesAKey(string method)
    {
        var guard = typeof(IssueExpediteController)
            .GetMethod(method)!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

        // No scope named: priority decides what the loop reaches for first, so
        // a key that could set one could put its own ticket at the front of
        // every night.
        Assert.NotNull(guard);
        Assert.Null(guard.AcceptScope);
    }

    [Fact]
    public async Task ReadingIt_IsOpenToAKey()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PriorityAsync(issue.Key, PriorityLevels.EmergencyName);

        // It rides IssueDto and IssueCardDto, both of which IssuesController
        // hands out to the hatch scope: an agent is entitled to know why it was
        // sent where it was sent.
        var read = await h.ReadAsync(issue.Key);
        Assert.True(read.Expedited);
        Assert.Equal(PriorityLevels.EmergencyName, read.Priority);

        var cards = Value(await h.Issues.SearchIssues(null, null, null, null, null, null, default));
        var card = cards.Single(c => c.Key == issue.Key);
        Assert.True(card.Expedited);
        Assert.Equal(PriorityLevels.EmergencyName, card.Priority);
    }

    [Fact]
    public async Task TheFlagRidesTheBoardCard()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PriorityAsync(issue.Key, PriorityLevels.ExpeditedName);

        var board = Value(await h.Board.GetBoard(default));

        var card = board.Issues.Single(c => c.Key == issue.Key);
        Assert.True(card.Expedited);
        Assert.Equal(PriorityLevels.ExpeditedName, card.Priority);
    }

    // ---- The float ----

    [Fact]
    public async Task AnExpeditedCard_IsServedAboveEveryOtherCardInItsColumn()
    {
        var h = await NewAsync();
        await h.FileAsync(title: "first");
        await h.FileAsync(title: "second");
        var last = await h.FileAsync(title: "last");

        await h.PriorityAsync(last.Key, PriorityLevels.ExpeditedName);

        // The bottom card of the column, above the two that outrank it. The
        // ordering is the server's, so this is what a client that only slices
        // the list will draw.
        Assert.Equal([last.Key, "AER-1", "AER-2"], await h.ColumnAsync(h.InboxId));
    }

    [Fact]
    public async Task AnEmergencyCard_IsServedAboveAnExpeditedCard()
    {
        var h = await NewAsync();
        var expedited = await h.FileAsync(title: "expedited");
        await h.FileAsync(title: "second");
        var emergency = await h.FileAsync(title: "emergency");

        await h.PriorityAsync(expedited.Key, PriorityLevels.ExpeditedName);
        await h.PriorityAsync(emergency.Key, PriorityLevels.EmergencyName);

        // Emergency first, then expedited, then everything else - the same
        // (Rank, Id) order within each level.
        Assert.Equal([emergency.Key, expedited.Key, "AER-2"], await h.ColumnAsync(h.InboxId));
    }

    [Fact]
    public async Task TwoCardsAtTheSameLevel_KeepTheBoardsOwnOrderBetweenThem()
    {
        var h = await NewAsync();
        var first = await h.FileAsync(title: "first");
        await h.FileAsync(title: "second");
        var last = await h.FileAsync(title: "last");

        await h.PriorityAsync(last.Key, PriorityLevels.ExpeditedName);
        await h.PriorityAsync(first.Key, PriorityLevels.ExpeditedName);

        // (Rank, Id) inside the expedited group as well as outside it - the
        // float is one more key in front of the tuple, not a replacement for
        // it.
        Assert.Equal([first.Key, last.Key, "AER-2"], await h.ColumnAsync(h.InboxId));
    }

    [Fact]
    public async Task Unmarking_ReturnsTheCardToItsRankPosition()
    {
        var h = await NewAsync();
        await h.FileAsync(title: "first");
        await h.FileAsync(title: "second");
        var last = await h.FileAsync(title: "last");

        await h.PriorityAsync(last.Key, PriorityLevels.EmergencyName);
        await h.PriorityAsync(last.Key, PriorityLevels.NormalName);

        Assert.Equal(["AER-1", "AER-2", last.Key], await h.ColumnAsync(h.InboxId));
    }

    [Fact]
    public async Task TheFloat_IsWithinAColumnAndNotAcrossTheBoard()
    {
        var h = await NewAsync();
        var here = await h.FileAsync(title: "here");
        var there = await h.FileAsync(title: "there");
        Value(await h.Issues.MoveIssue(there.Key, new IssueMoveRequest(h.DoneId, null, null), default));

        await h.PriorityAsync(there.Key, PriorityLevels.EmergencyName);

        // Priority is a sort key inside a column. Where the columns themselves
        // sit is the status's SortOrder, and nothing about a card moves that.
        Assert.Equal([here.Key], await h.ColumnAsync(h.InboxId));
        Assert.Equal([there.Key], await h.ColumnAsync(h.DoneId));
    }

    [Fact]
    public async Task AnInheritedLevel_FloatsTheSameAsAnOwnOne()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        await h.PriorityAsync(epic.Key, PriorityLevels.ExpeditedName);
        var child = await h.FileAsync("story", "the story", parentKey: epic.Key);
        var unrelated = await h.FileAsync("story", "unrelated");

        // The child's own level stays Normal - it is the epic's Expedited that
        // floats it, exactly as AnExpeditedCard_IsServedAboveEveryOtherCardInItsColumn
        // proves for a card expedited on its own row.
        Assert.Equal([epic.Key, child.Key, unrelated.Key], await h.ColumnAsync(h.InboxId));
    }

    [Fact]
    public async Task AnInheritedLevel_IsNotOutrankedByRank()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        await h.PriorityAsync(epic.Key, PriorityLevels.ExpeditedName);
        var child = await h.FileAsync("story", "the story", parentKey: epic.Key);
        var unrelated = await h.FileAsync("story", "unrelated");

        // Drop the unrelated card to the top of the rank order - ahead of the
        // epic's child by rank alone.
        Value(await h.Issues.MoveIssue(unrelated.Key, new IssueMoveRequest(h.InboxId, null, child.Key), default));

        // The inherited level still wins: rank is not enough to place a lower
        // effective level above a higher one, inherited or own.
        Assert.Equal([epic.Key, child.Key, unrelated.Key], await h.ColumnAsync(h.InboxId));
    }

    // ---- The legacy alias ----

    [Fact]
    public async Task TheExpediteRoute_SetsExpeditedByName()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var marked = await h.ExpediteAsync(issue.Key, true);

        Assert.True(marked.Expedited);
        Assert.Equal(PriorityLevels.Expedited, (await h.RowAsync(issue.Key)).Priority);
    }

    [Fact]
    public async Task TheExpediteRoutesFalse_SetsNormalByName()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PriorityAsync(issue.Key, PriorityLevels.EmergencyName);

        var unmarked = await h.ExpediteAsync(issue.Key, false);

        Assert.False(unmarked.Expedited);
        Assert.Equal(PriorityLevels.Normal, (await h.RowAsync(issue.Key)).Priority);
    }

    [Fact]
    public async Task BothRoutesWriteTheSameTrail()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.ExpediteAsync(issue.Key, true);

        var e = Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal((PriorityLevels.NormalName, PriorityLevels.ExpeditedName), Sides(e));
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required IssueExpediteController Expedite { get; init; }
        public required IssueExpediteController Priority { get; init; }
        public required IssuesController Issues { get; init; }
        public required BoardController Board { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required int InboxId { get; init; }
        public required int DoneId { get; init; }

        public async Task<IssueDto> FileAsync(string type = "task", string title = "a thing", string? parentKey = null) =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, parentKey, null, null), default));

        public async Task<IssueDto> PriorityAsync(string key, string level) =>
            Value(await Priority.PutIssuePriority(key, new PriorityRequest(level), default));

        public async Task<IssueDto> ExpediteAsync(string key, bool expedited) =>
            Value(await Expedite.PutIssueExpedite(key, new ExpediteRequest(expedited), default));

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        public async Task<IssueDto> PatchAsync(string key, IssuePatchRequest patch) =>
            Value(await Issues.PatchIssue(key, patch, default));

        /// <summary>One column of the board, in the order the server served it.</summary>
        public async Task<IReadOnlyList<string>> ColumnAsync(int statusId) =>
            Value(await Board.GetBoard(default)).Issues
                .Where(c => c.StatusId == statusId)
                .Select(c => c.Key)
                .ToList();

        /// <summary>The row itself, which is what the projection is read against.</summary>
        public async Task<EfHatchIssue> RowAsync(string key)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            return await Db.Issues.AsNoTracking().WithKey(projectKey, number).FirstAsync();
        }

        /// <summary>The level's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.PriorityChanged)
                .Reverse()
                .ToList();
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        db.Add(inbox);
        var done = new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true };
        db.Add(done);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();
        var controller = new IssueExpediteController(db, actors, TestClaims.With(), caller, time);

        return new Harness
        {
            Db = db,
            // One controller instance, called through two names - both routes
            // share the one route class, exactly as they share it in production.
            Expedite = controller,
            Priority = controller,
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time),
            Board = new BoardController(db, actors, TestClaims.With(), time),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = hatch.Id,
            InboxId = inbox.Id,
            DoneId = done.Id,
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

        /// <summary>Local mode's third lane: the person at the machine, or a runner that named itself. Null is every install with a wall.</summary>
        public Actor? Local { get; set; }

        public Task<Actor?> LocalAsync(CancellationToken ct) => Task.FromResult(Local);

        public Task<bool> IsProgramAsync(CancellationToken ct) =>
            Task.FromResult(Key is not null || Local is { Kind: ActorKind.Key });

        public Task<string> ActorNameAsync(CancellationToken ct) =>
            Task.FromResult(Person?.Name ?? Key?.Name ?? Local?.Name ?? CallerIdentity.Unattributed);
    }

    /// <summary>The two sides a <c>priority_changed</c> event carries, by name.</summary>
    private static (string From, string To) Sides(IssueEventDto e)
    {
        var payload = e.Payload!.Value;
        return (payload.GetProperty("from").GetString()!, payload.GetProperty("to").GetString()!);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    /// <summary>The plain-text reason on a refusal - what the UI puts on screen.</summary>
    private static string Reason(IActionResult? result) => result switch
    {
        ObjectResult o => o.Value?.ToString() ?? $"{o.StatusCode}",
        StatusCodeResult s => s.StatusCode.ToString(),
        null => "no result",
        _ => result.GetType().Name,
    };
}

using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What the route writes, what it refuses to write twice, what nothing else is
/// allowed to clear - and the property the whole design rests on, which is
/// that an API key cannot set one.
/// </summary>
public class IssuePreApprovalControllerTests
{
    // ---- Marking, and unmarking ----

    [Fact]
    public async Task AnIssue_IsNotExpressUntilSomebodySaysSo()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.False(issue.Express);
    }

    [Fact]
    public async Task AnIssue_IsMarkedExpress()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var marked = await h.ExpressAsync(issue.Key, true);

        Assert.True(marked.Express);
        Assert.True((await h.RowAsync(issue.Key)).PreApproval >= PreApprovalLevels.Express);
    }

    [Fact]
    public async Task TheSameControl_UnmarksIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.ExpressAsync(issue.Key, true);
        var unmarked = await h.ExpressAsync(issue.Key, false);

        Assert.False(unmarked.Express);
        Assert.False((await h.RowAsync(issue.Key)).PreApproval >= PreApprovalLevels.Express);
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.Express.PutIssueExpress("AER-404", new ExpressRequest(true), default)).Result);
    }

    // ---- The trail ----

    [Fact]
    public async Task Marking_WritesWhoDidItAndWhichWay()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.ExpressAsync(issue.Key, true);

        var e = Assert.Single(await h.EventsAsync(issue.Key));
        Assert.Equal("Nathan", e.Actor);
        Assert.Equal(("none", "express"), Sides(e));
    }

    [Fact]
    public async Task Unmarking_WritesItsOwnRowTheOtherWayRound()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.ExpressAsync(issue.Key, true);
        await h.ExpressAsync(issue.Key, false);

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);
        Assert.Equal(("none", "express"), Sides(events[0]));
        Assert.Equal(("express", "none"), Sides(events[1]));
    }

    // ---- Setting what it already holds ----

    [Fact]
    public async Task SettingTheValueItAlreadyHolds_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ExpressAsync(issue.Key, true);

        var again = await h.ExpressAsync(issue.Key, true);

        Assert.True(again.Express);
        Assert.Single(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task SettingTheValueItAlreadyHolds_DoesNotMoveTheUpdatedTime()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ExpressAsync(issue.Key, true);

        var was = (await h.ReadAsync(issue.Key)).UpdatedAt;
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.ExpressAsync(issue.Key, true);

        Assert.Equal(was, (await h.ReadAsync(issue.Key)).UpdatedAt);
    }

    [Fact]
    public async Task Marking_WritesNothingThatTouchesPriority()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.ExpressAsync(issue.Key, true);

        // Filing writes its own created event - beside that, this route
        // writes exactly one pre_approval_changed event and nothing naming
        // priority, which is a different row entirely (IssueExpediteController's).
        var events = Value(await h.Thread.GetEvents(issue.Key, default));
        Assert.DoesNotContain(events, e => e.Kind == EfHatchIssueEvent.PriorityChanged);
        Assert.Single(events, e => e.Kind == EfHatchIssueEvent.PreApprovalChanged);
        Assert.Equal(PriorityLevels.Normal, (await h.RowAsync(issue.Key)).Priority);
    }

    [Fact]
    public async Task UnmarkingSomethingNeverMarked_WritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var unmarked = await h.ExpressAsync(issue.Key, false);

        Assert.False(unmarked.Express);
        Assert.Empty(await h.EventsAsync(issue.Key));
    }

    // ---- Nothing else clears it ----

    [Fact]
    public async Task Moving_RetitlingAndReparenting_LeaveItAsItWasSet()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic", "the epic");
        var issue = await h.FileAsync("story", "the story");
        await h.ExpressAsync(issue.Key, true);

        // Every other way an issue is written, one after another. The flag is
        // written by one route and read by everything, so "nothing clears it"
        // is a claim about every other route rather than about this one.
        await h.PatchAsync(issue.Key, new IssuePatchRequest(
            Title: "renamed", Description: null, Type: null, StatusId: null, ParentKey: epic.Key,
            ReadyAt: null, DueAt: null, PullRequestUrl: null, ProjectId: null, MoveDescendants: null));

        var moved = Value(await h.Issues.MoveIssue(
            issue.Key, new IssueMoveRequest(h.DoneId, null, null), default));

        Assert.True(moved.Express);
        Assert.True((await h.RowAsync(issue.Key)).PreApproval >= PreApprovalLevels.Express);
    }

    // ---- The edge that is cut ----

    [Fact]
    public void SettingIt_IsClosedToAnApiKey()
    {
        var guard = typeof(IssuePreApprovalController)
            .GetMethod(nameof(IssuePreApprovalController.PutIssueExpress))!
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .SingleOrDefault();

        // No scope named, and no class-level attribute to inherit one from:
        // express decides which gates the loop may pass unattended, so a key
        // that could set one could carry its own ticket through the night
        // unattended.
        Assert.NotNull(guard);
        Assert.Null(guard.AcceptScope);

        Assert.Empty(typeof(IssuePreApprovalController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false));
    }

    [Fact]
    public async Task ReadingIt_IsOpenToAKey()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ExpressAsync(issue.Key, true);

        // It rides IssueDto and IssueCardDto, both of which IssuesController
        // hands out to the hatch scope: an agent is entitled to know why it was
        // sent where it was sent.
        Assert.True((await h.ReadAsync(issue.Key)).Express);

        var cards = Value(await h.Issues.SearchIssues(null, null, null, null, null, null, default));
        Assert.True(cards.Single(c => c.Key == issue.Key).Express);
    }

    [Fact]
    public async Task TheFlagRidesTheBoardCard()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.ExpressAsync(issue.Key, true);

        var board = Value(await h.Board.GetBoard(default));

        Assert.True(board.Issues.Single(c => c.Key == issue.Key).Express);
    }

    /// <summary>
    /// The opposite of expedite's own test here: express is a gate-passer, not
    /// a sort key, so a card carrying it sits exactly where it would sit
    /// without the flag.
    /// </summary>
    [Fact]
    public async Task AnExpressCard_IsServedExactlyWhereItWouldBeWithoutTheFlag()
    {
        var h = await NewAsync();
        await h.FileAsync(title: "first");
        await h.FileAsync(title: "second");
        var last = await h.FileAsync(title: "last");

        await h.ExpressAsync(last.Key, true);

        Assert.Equal(["AER-1", "AER-2", last.Key], await h.ColumnAsync(h.InboxId));
    }

    [Fact]
    public async Task AKeylessRunnerWhereTheWallIsOff_IsTheSame403()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");

        var refusal = await h.Express.PutIssueExpress(issue.Key, new ExpressRequest(true), default);

        Assert.Equal(403, ((ObjectResult)refusal.Result!).StatusCode);
        Assert.False((await h.RowAsync(issue.Key)).PreApproval >= PreApprovalLevels.Express);
    }

    // ---- The seam ----

    [Fact]
    public async Task ANonMember_IsRefusedExpressTheSameSentenceTheSiblingMoveTestGets()
    {
        var h = await NewAsync(seam: true);
        var issue = await h.FileAsync();
        h.Actors.Me = h.Actors.AddPerson("Ada");

        var refusal = await h.Express.PutIssueExpress(issue.Key, new ExpressRequest(true), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal("only an owner or approver of AER may move work into \"review\"", obj.Value);
    }

    [Theory]
    [InlineData(ProjectMemberRole.Owner)]
    [InlineData(ProjectMemberRole.Approver)]
    public async Task AnOwnerOrApprover_MaySetExpress(string role)
    {
        var h = await NewAsync(seam: true);
        var issue = await h.FileAsync();
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(ada.Id, role);
        h.Actors.Me = ada;

        var marked = await h.ExpressAsync(issue.Key, true);

        Assert.True(marked.Express);
    }

    [Fact]
    public async Task ANonMember_IsRefusedClearingExpressTheSameSentence()
    {
        var h = await NewAsync(seam: true);
        var issue = await h.FileAsync();
        await h.PreApprovalAsync(issue.Key, PreApprovalLevels.Express);
        h.Actors.Me = h.Actors.AddPerson("Ada");

        var refusal = await h.Express.PutIssueExpress(issue.Key, new ExpressRequest(false), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal("only an owner or approver of AER may move work into \"review\"", obj.Value);
    }

    [Theory]
    [InlineData(ProjectMemberRole.Owner)]
    [InlineData(ProjectMemberRole.Approver)]
    public async Task AnOwnerOrApprover_MayClearExpress(string role)
    {
        var h = await NewAsync(seam: true);
        var issue = await h.FileAsync();
        await h.PreApprovalAsync(issue.Key, PreApprovalLevels.Express);
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(ada.Id, role);
        h.Actors.Me = ada;

        var unmarked = await h.ExpressAsync(issue.Key, false);

        Assert.False(unmarked.Express);
    }

    [Fact]
    public async Task UntickingProtectedDirectlyOnTheRow_LetsANonMemberThrough()
    {
        var h = await NewAsync(seam: true);
        var issue = await h.FileAsync();
        var review = await h.Db.Statuses.FirstAsync(s => s.Id == h.ReviewId);
        review.IsProtected = false;
        await h.Db.SaveChangesAsync();
        h.Actors.Me = h.Actors.AddPerson("Ada");

        var marked = await h.ExpressAsync(issue.Key, true);
        Assert.True(marked.Express);

        var unmarked = await h.ExpressAsync(issue.Key, false);
        Assert.False(unmarked.Express);
    }

    [Fact]
    public async Task UntickingProtectedThroughTheRoute_LetsANonMemberThrough()
    {
        var h = await NewAsync(seam: true);
        var issue = await h.FileAsync();
        await h.Statuses.PutProtected(h.ReviewId!.Value, new ProtectedRequest(false), default);
        h.Actors.Me = h.Actors.AddPerson("Ada");

        var marked = await h.ExpressAsync(issue.Key, true);
        Assert.True(marked.Express);

        var unmarked = await h.ExpressAsync(issue.Key, false);
        Assert.False(unmarked.Express);
    }

    // ---- The approver gate ----

    [Fact]
    public async Task ANonMember_IsRefusedSettingPreapproved()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        h.Actors.Me = h.Actors.AddPerson("Ada");

        var refusal = await h.Express.PutIssuePreApproval(
            issue.Key, new PreApprovalRequest(PreApprovalLevels.PreApprovedName), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal(
            "only an owner or approver of AER may set pre-approval - it is the approve press given in advance",
            obj.Value);
    }

    [Theory]
    [InlineData(ProjectMemberRole.Owner)]
    [InlineData(ProjectMemberRole.Approver)]
    public async Task AnOwnerOrApprover_MaySetPreapproved(string role)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(ada.Id, role);
        h.Actors.Me = ada;

        var marked = Value(await h.Express.PutIssuePreApproval(
            issue.Key, new PreApprovalRequest(PreApprovalLevels.PreApprovedName), default));

        Assert.Equal(PreApprovalLevels.PreApprovedName, marked.PreApproval);
    }

    [Fact]
    public async Task ANonApprover_CannotLowerFromPreapprovedToExpress()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PreApprovalAsync(issue.Key, PreApprovalLevels.PreApproved);
        h.Actors.Me = h.Actors.AddPerson("Ada");

        var refusal = await h.Express.PutIssuePreApproval(
            issue.Key, new PreApprovalRequest(PreApprovalLevels.ExpressName), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal(
            "only an owner or approver of AER may set pre-approval - it is the approve press given in advance",
            obj.Value);
    }

    [Fact]
    public async Task ANonApprover_CannotLowerFromPreapprovedToNone()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PreApprovalAsync(issue.Key, PreApprovalLevels.PreApproved);
        h.Actors.Me = h.Actors.AddPerson("Ada");

        var refusal = await h.Express.PutIssuePreApproval(
            issue.Key, new PreApprovalRequest(PreApprovalLevels.NoneName), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal(
            "only an owner or approver of AER may set pre-approval - it is the approve press given in advance",
            obj.Value);
    }

    [Theory]
    [InlineData(PreApprovalLevels.ExpressName)]
    [InlineData(PreApprovalLevels.NoneName)]
    public async Task AnApprover_MayLowerFromPreapprovedToExpressOrNone(string target)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PreApprovalAsync(issue.Key, PreApprovalLevels.PreApproved);
        var ada = h.Actors.AddPerson("Ada");
        await h.MemberAsync(ada.Id, ProjectMemberRole.Approver);
        h.Actors.Me = ada;

        var lowered = Value(await h.Express.PutIssuePreApproval(
            issue.Key, new PreApprovalRequest(target), default));

        Assert.Equal(target, lowered.PreApproval);
    }

    [Fact]
    public async Task LoweringFromPreapproved_IsNotAffectedByWhetherReviewIsProtected()
    {
        var h = await NewAsync(seam: true);
        var issue = await h.FileAsync();
        await h.PreApprovalAsync(issue.Key, PreApprovalLevels.PreApproved);
        h.Actors.Me = h.Actors.AddPerson("Ada");

        var refusal = await h.Express.PutIssuePreApproval(
            issue.Key, new PreApprovalRequest(PreApprovalLevels.NoneName), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal(
            "only an owner or approver of AER may set pre-approval - it is the approve press given in advance",
            obj.Value);
    }

    // ---- The legacy alias ----

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required StubActorDirectory Actors { get; init; }
        public required IssuePreApprovalController Express { get; init; }
        public required IssuesController Issues { get; init; }
        public required BoardController Board { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required StatusesController Statuses { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required int InboxId { get; init; }
        public required int DoneId { get; init; }
        public int? ReviewId { get; init; }

        public async Task<IssueDto> FileAsync(string type = "task", string title = "a thing") =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, type, title, null, null, null, null), default));

        public async Task<IssueDto> ExpressAsync(string key, bool express) =>
            Value(await Express.PutIssueExpress(key, new ExpressRequest(express), default));

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

        /// <summary>The flag's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.PreApprovalChanged)
                .Reverse()
                .ToList();

        /// <summary>A person's role on a project - what <see cref="ProjectAccess"/> reads to answer owner/approver.</summary>
        public async Task MemberAsync(Guid personId, string role, int? projectId = null)
        {
            Db.ProjectMembers.Add(new EfHatchProjectMember
            {
                ProjectId = projectId ?? ProjectId,
                PersonId = personId,
                Role = role,
                CreatedAt = Now,
            });
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// Written straight to the row, the same as <see cref="RowAsync"/> reads -
        /// for a test that needs an issue already at <c>PreApproved</c> or
        /// <c>Express</c> to set up a lowering or clearing test. The setup is
        /// about the state, not about how it got there, so it bypasses the gate
        /// instead of requiring an approver just to arrange the fixture.
        /// </summary>
        public async Task PreApprovalAsync(string key, int level)
        {
            IssueKey.TryParse(key, out var projectKey, out var number);
            var issue = await Db.Issues.WithKey(projectKey, number).FirstAsync();
            issue.PreApproval = level;
            await Db.SaveChangesAsync();
        }
    }

    private static async Task<Harness> NewAsync(bool seam = false)
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        db.Add(inbox);
        EfHatchStatus done;
        EfHatchStatus? review = null;
        if (seam)
        {
            // The seam's own board: a protected review column immediately
            // before the implementation column, so Columns.SendBack measures
            // back to "review" rather than resolving to null the way it does
            // on the plain two-status board every other test here runs on.
            review = new EfHatchStatus { Name = "review", SortOrder = 20, IsProtected = true };
            var doing = new EfHatchStatus { Name = "doing", SortOrder = 30, IsImplementation = true };
            done = new EfHatchStatus { Name = "done", SortOrder = 40, IsTerminal = true };
            db.AddRange(review, doing, done);
        }
        else
        {
            done = new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true };
            db.Add(done);
        }
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Actors = actors,
            Express = new IssuePreApprovalController(db, actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Board = new BoardController(db, actors, TestClaims.With(), time, TestClaims.DoneHorizon()),
            Thread = new IssueThreadController(db, caller, time),
            Statuses = new StatusesController(db, caller, time, NullLogger<StatusesController>.Instance),
            Time = time,
            ProjectId = hatch.Id,
            InboxId = inbox.Id,
            DoneId = done.Id,
            ReviewId = review?.Id,
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

    /// <summary>The two sides a <c>pre_approval_changed</c> event carries, by name.</summary>
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

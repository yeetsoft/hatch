using System.Reflection;
using System.Text.Json;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Hatch.Api.Services.Chat;
using Hatch.Api.Tests.Chat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What setting a question's level writes, what it refuses, and the property
/// the design rests on: an API key cannot set one.
/// </summary>
public class QuestionLevelControllerTests
{
    private static readonly QuestionOptionDto[] Recommended =
        [new("Take this one", null, true), new("Take that one")];

    private static readonly QuestionOptionDto[] NoneRecommended =
        [new("This one"), new("That one")];

    // ---- Setting ----

    [Theory]
    [InlineData(QuestionLevels.BuildName)]
    [InlineData(QuestionLevels.MergeName)]
    public async Task ALevel_IsSetAndEveryQuestionRouteReadsIt(string level)
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key, Recommended);

        var set = await h.SetAsync(key, id, level);

        Assert.Equal(level, set.Level);
        Assert.Equal(level, Value(await h.Questions.GetIssueQuestions(key, true, default)).Single().Level);
        Assert.Equal(level, Value(await h.Questions.GetQuestions(true, default)).Single().Level);
    }

    [Fact]
    public async Task AQuestion_ReadsHoldUntilSomebodySaysSo()
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        await h.AskAsync(key, Recommended);

        Assert.Equal(QuestionLevels.HoldName, Value(await h.Questions.GetIssueQuestions(key, true, default)).Single().Level);
    }

    [Fact]
    public async Task AChange_WritesOneEventWithBothLevelsAndThePerson()
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key, Recommended);

        await h.SetAsync(key, id, QuestionLevels.BuildName);
        await h.SetAsync(key, id, QuestionLevels.MergeName);

        var events = await h.EventsAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal("Nathan", e.Actor));
        Assert.Equal((id, "hold", "build"), Sides(events[0]));
        Assert.Equal((id, "build", "merge"), Sides(events[1]));
    }

    [Fact]
    public async Task TheLevelAlreadyHeld_WritesNothing()
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key, Recommended);
        var updated = (await h.IssueAsync(key)).UpdatedAt;

        var same = await h.SetAsync(key, id, QuestionLevels.HoldName);
        Assert.Equal(QuestionLevels.HoldName, same.Level);
        Assert.Empty(await h.EventsAsync());

        await h.SetAsync(key, id, QuestionLevels.BuildName);
        h.Time.Advance(TimeSpan.FromHours(1));
        await h.SetAsync(key, id, QuestionLevels.BuildName);

        Assert.Single(await h.EventsAsync());
        Assert.Equal(updated, (await h.IssueAsync(key)).UpdatedAt);
    }

    [Fact]
    public async Task HoldIsAlwaysAllowed_EvenWithNothingRecommended()
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key, Recommended);
        await h.SetAsync(key, id, QuestionLevels.MergeName);

        // Strip the recommendation behind the route's back, as an edited row would be.
        var row = await h.Db.Comments.SingleAsync(c => c.Id == id);
        row.Options = Questions.WriteOptions(NoneRecommended);
        await h.Db.SaveChangesAsync();

        var lowered = await h.SetAsync(key, id, QuestionLevels.HoldName);

        Assert.Equal(QuestionLevels.HoldName, lowered.Level);
    }

    // ---- Refusals ----

    [Theory]
    [InlineData("always")]
    [InlineData("")]
    [InlineData("BUILD")]
    public async Task ANameThatIsNotALevel_Is400(string level)
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key, Recommended);

        var result = await h.Level.PutQuestionLevel(key, id, new QuestionLevelRequest(level), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"\"{level}\" is not a question level - hold, build or merge", bad.Value);
        Assert.Empty(await h.EventsAsync());
    }

    [Theory]
    [InlineData(QuestionLevels.BuildName)]
    [InlineData(QuestionLevels.MergeName)]
    public async Task AProseQuestion_IsRefusedAboveHoldAndAcceptedAtHold(string level)
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key, null);

        var bad = Assert.IsType<BadRequestObjectResult>(
            (await h.Level.PutQuestionLevel(key, id, new QuestionLevelRequest(level), default)).Result);

        Assert.Contains("nothing to build on", (string)bad.Value!);
        Assert.Empty(await h.EventsAsync());
        Assert.Equal(QuestionLevels.HoldName, (await h.SetAsync(key, id, QuestionLevels.HoldName)).Level);
    }

    [Theory]
    [InlineData(QuestionLevels.BuildName)]
    [InlineData(QuestionLevels.MergeName)]
    public async Task OptionsWithNoRecommendation_AreRefusedAboveHoldAndAcceptedAtHold(string level)
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key, NoneRecommended);

        var bad = Assert.IsType<BadRequestObjectResult>(
            (await h.Level.PutQuestionLevel(key, id, new QuestionLevelRequest(level), default)).Result);

        Assert.Contains("nothing to build on", (string)bad.Value!);
        Assert.Empty(await h.EventsAsync());
        Assert.Equal(QuestionLevels.HoldName, (await h.SetAsync(key, id, QuestionLevels.HoldName)).Level);
    }

    [Fact]
    public async Task AnAnsweredQuestion_Is400()
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key, Recommended);
        Created(await h.Thread.AddComment(key, new CommentCreateRequest("Take this one", "answer", id), default));

        var result = await h.Level.PutQuestionLevel(key, id, new QuestionLevelRequest("build"), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("already answered", (string)bad.Value!);
        Assert.Empty(await h.EventsAsync());
    }

    [Fact]
    public async Task ACommentThatIsNotAQuestionOnThatIssue_Is400()
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var other = await h.FileAsync();
        var note = Created(await h.Thread.AddComment(key, new CommentCreateRequest("just a note"), default)).Id;
        var elsewhere = await h.AskAsync(other, Recommended);

        foreach (var id in new[] { note, elsewhere, 9_999L })
        {
            var result = await h.Level.PutQuestionLevel(key, id, new QuestionLevelRequest("build"), default);

            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal($"comment {id} is not a question on {key}", bad.Value);
        }
        Assert.Empty(await h.EventsAsync());
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.Level.PutQuestionLevel("AER-404", 1, new QuestionLevelRequest("hold"), default)).Result);
    }

    // ---- Keys ----

    [Fact]
    public async Task AProgramCaller_GetsA403AndNothingIsWritten()
    {
        var h = await NewAsync();
        var key = await h.FileAsync();
        var id = await h.AskAsync(key, Recommended);
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");

        var refusal = await h.Level.PutQuestionLevel(key, id, new QuestionLevelRequest("build"), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal("setting a question's level is a person's call, not an agent's", obj.Value);
        Assert.Null((await h.Db.Comments.AsNoTracking().SingleAsync(c => c.Id == id)).Level);
        Assert.Empty(await h.EventsAsync());
    }

    [Fact]
    public async Task WithChatWired_AChangeEditsTheQuestionsRoomMessage()
    {
        var h = await NewAsync();
        var seed = new ChatOutboxSeed(h.Db, Now);
        var workspace = await seed.WorkspaceAsync("TACME", h.ProjectId);
        await seed.RoomAsync(workspace, "C1", questions: true);
        var outbox = new ChatOutbox(h.Db, h.Time);
        var links = new ChatLinks(Options.Create(new HatchOptions()));
        var controller = new QuestionLevelController(h.Db, h.Caller, h.Time, outbox, links);

        var key = await h.FileAsync();
        var id = await h.AskAsync(key, Recommended);
        var issue = await h.Db.Issues.AsNoTracking().SingleAsync(i => i.Project!.Key == "AER");
        await outbox.QueueAsync(
            ChatMessageKind.Question, $"comment:{id}", h.ProjectId,
            QuestionChatContent.Build(links, id, key, issue.Title, "Which way?", Recommended, QuestionLevels.HoldName),
            default);
        await h.Db.SaveChangesAsync();
        var before = await h.Db.ChatMessages.SingleAsync();

        Value(await controller.PutQuestionLevel(key, id, new QuestionLevelRequest(QuestionLevels.BuildName), default));

        var after = await h.Db.ChatMessages.SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(2, after.Revision);
        Assert.Equal("Level: build", ChatMessageContent.Read(after.Content)!.Lines.Single(l => l.StartsWith("Level:")));
    }

    [Fact]
    public void PutQuestionLevel_CarriesAPersonOnlyRoleWithNoAcceptKey()
    {
        var method = typeof(QuestionLevelController).GetMethod(nameof(QuestionLevelController.PutQuestionLevel))!;
        var attribute = method.GetCustomAttribute<RequireRoleAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(PersonRole.User, attribute!.Minimum);
        Assert.False(attribute.AcceptKey);
        Assert.Null(typeof(QuestionLevelController).GetCustomAttribute<RequireRoleAttribute>());
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required QuestionLevelController Level { get; init; }
        public required QuestionsController Questions { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<string> FileAsync() =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", "a thing", null, null, null, null), default)).Key;

        public async Task<long> AskAsync(string key, IReadOnlyList<QuestionOptionDto>? options) =>
            Created(await Thread.AddComment(key, new CommentCreateRequest("Which way?", "question", Options: options), default)).Id;

        public async Task<QuestionDto> SetAsync(string key, long id, string level) =>
            Value(await Level.PutQuestionLevel(key, id, new QuestionLevelRequest(level), default));

        public async Task<IssueDto> IssueAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>Every <c>question_level_changed</c> event in the house, oldest first.</summary>
        public async Task<IReadOnlyList<EfHatchIssueEvent>> EventsAsync() =>
            (await Db.IssueEvents.AsNoTracking()
                .Where(e => e.Kind == EfHatchIssueEvent.QuestionLevelChanged)
                .ToListAsync())
            .OrderBy(e => e.Id)
            .ToList();
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var project = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(project);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        db.Add(new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Caller = caller,
            Level = new QuestionLevelController(db, caller, time),
            Questions = new QuestionsController(db),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = project.Id,
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

    /// <summary>The question and both sides a <c>question_level_changed</c> event carries.</summary>
    private static (long QuestionId, string From, string To) Sides(EfHatchIssueEvent e)
    {
        var payload = JsonDocument.Parse(e.Payload!).RootElement;
        return (payload.GetProperty("questionId").GetInt64(), payload.GetProperty("from").GetString()!, payload.GetProperty("to").GetString()!);
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
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

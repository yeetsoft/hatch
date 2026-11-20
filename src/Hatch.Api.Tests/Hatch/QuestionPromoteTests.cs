using System.Reflection;
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
/// What promoting an answered question's answer into a principle writes,
/// what it refuses, and the property the design rests on: an API key cannot
/// promote one.
/// </summary>
public class QuestionPromoteTests
{
    // ---- Promoting ----

    [Fact]
    public async Task ADefaultSentence_IsTheAnswersBody()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var id = await h.AskAsync(ambition, "Which way?");
        await h.AnswerAsync(ambition, id, "least surprise over least typing");

        var promoted = await h.PromoteAsync(ambition, id);

        Assert.Equal("least surprise over least typing", promoted.Principle);
        Assert.Contains("- least surprise over least typing", (await h.IssueAsync(ambition)).Description);
    }

    [Fact]
    public async Task ASentenceGiven_OverridesTheAnswer()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var id = await h.AskAsync(ambition, "Which way?");
        await h.AnswerAsync(ambition, id, "the answer's own words");

        var promoted = await h.PromoteAsync(ambition, id, "a sentence given instead");

        Assert.Equal("a sentence given instead", promoted.Principle);
    }

    [Fact]
    public async Task AnExistingSection_GetsTheBulletAfterTheLastOne_AndTheHeadingBelowIsLeftAlone()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        await h.SetDescriptionAsync(ambition, """
            Some prose up top.

            ## Principles

            - per-node, not per-run

            ## Non-goals

            - not this
            """);
        var id = await h.AskAsync(ambition, "Which way?");
        await h.AnswerAsync(ambition, id, "least surprise over least typing");

        await h.PromoteAsync(ambition, id);

        var description = (await h.IssueAsync(ambition)).Description;
        Assert.Equal(
            ["per-node, not per-run", "least surprise over least typing"],
            Principles.Read(description));
        Assert.Contains("## Non-goals", description);
        Assert.Contains("- not this", description);
    }

    [Fact]
    public async Task NoHeading_AddsOneAtTheEnd()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        await h.SetDescriptionAsync(ambition, "Just a short ambition, with no heading at all.");
        var id = await h.AskAsync(ambition, "Which way?");
        await h.AnswerAsync(ambition, id, "say why, not just what");

        await h.PromoteAsync(ambition, id);

        Assert.Equal(["say why, not just what"], Principles.Read((await h.IssueAsync(ambition)).Description));
    }

    [Fact]
    public async Task ATwoLineAnswer_BecomesOneBullet()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var id = await h.AskAsync(ambition, "Which way?");
        await h.AnswerAsync(ambition, id, "the first line\nand a second, as a continuation");

        var promoted = await h.PromoteAsync(ambition, id);

        Assert.Equal("the first line and a second, as a continuation", promoted.Principle);
        Assert.Equal(["the first line and a second, as a continuation"], Principles.Read((await h.IssueAsync(ambition)).Description));
    }

    [Fact]
    public async Task AQuestionOnTheAmbitionItself_Promotes()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var id = await h.AskAsync(ambition, "Which way?");
        await h.AnswerAsync(ambition, id, "a principle for the ambition itself");

        var promoted = await h.PromoteAsync(ambition, id);

        Assert.Equal(ambition, promoted.AmbitionKey);
    }

    [Fact]
    public async Task AQuestionOnAStoryTwoLevelsUnder_PromotesOntoTheAmbition()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var story = await h.FileAsync("story", epic);
        var id = await h.AskAsync(story, "Which way?");
        await h.AnswerAsync(story, id, "a principle from deep in the tree");

        var promoted = await h.PromoteAsync(story, id);

        Assert.Equal(ambition, promoted.AmbitionKey);
        Assert.Equal(["a principle from deep in the tree"], Principles.Read((await h.IssueAsync(ambition)).Description));
    }

    [Fact]
    public async Task BothEvents_AreOnTheAmbition_WithThePayload()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var id = await h.AskAsync(epic, "Which way?");
        await h.AnswerAsync(epic, id, "a principle worth keeping");

        await h.PromoteAsync(epic, id);

        var events = await h.EventsOnAsync(ambition);
        var redescribed = Assert.Single(events, e => e.Kind == EfHatchIssueEvent.Redescribed);
        var promoted = Assert.Single(events, e => e.Kind == EfHatchIssueEvent.PrinciplePromoted);

        Assert.All(events, e => Assert.Equal("Nathan", e.Actor));

        var redescribedPayload = JsonDocument.Parse(redescribed.Payload!).RootElement;
        Assert.Equal("", redescribedPayload.GetProperty("from").GetString());
        Assert.Contains("a principle worth keeping", redescribedPayload.GetProperty("to").GetString());

        var promotedPayload = JsonDocument.Parse(promoted.Payload!).RootElement;
        Assert.Equal(id, promotedPayload.GetProperty("questionId").GetInt64());
        Assert.Equal(epic, promotedPayload.GetProperty("issueKey").GetString());
        Assert.Equal("a principle worth keeping", promotedPayload.GetProperty("principle").GetString());
    }

    // ---- QuestionDto.AmbitionKey ----

    [Fact]
    public async Task AQuestion_UnderAnAmbition_CarriesItsKey()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var epic = await h.FileAsync("epic", ambition);
        var id = await h.AskAsync(epic, "Which way?");

        var question = Assert.Single(Value(await h.Questions.GetIssueQuestions(epic, true, default)));

        Assert.Equal(id, question.Id);
        Assert.Equal(ambition, question.AmbitionKey);
    }

    [Fact]
    public async Task AQuestion_WithNoAmbitionAbove_CarriesNull()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic");
        await h.AskAsync(epic, "Which way?");

        var question = Assert.Single(Value(await h.Questions.GetIssueQuestions(epic, true, default)));

        Assert.Null(question.AmbitionKey);
    }

    // ---- Refusals ----

    [Fact]
    public async Task AnUnansweredQuestion_Is400_AndChangesNothing()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var id = await h.AskAsync(ambition, "Which way?");
        var before = (await h.IssueAsync(ambition)).Description;

        var result = await h.Promote.Promote(ambition, id, new QuestionPromoteRequest(null), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"question {id} has no answer to promote", bad.Value);
        Assert.Equal(before, (await h.IssueAsync(ambition)).Description);
        Assert.Empty(await h.EventsOnAsync(ambition));
    }

    [Fact]
    public async Task ACommentThatIsNotAQuestionOnThatIssue_Is400()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var other = await h.FileAsync("ambition");
        var note = Created(await h.Thread.AddComment(ambition, new CommentCreateRequest("just a note"), default)).Id;
        var elsewhere = await h.AskAsync(other, "Which way?");

        foreach (var id in new[] { note, elsewhere, 9_999L })
        {
            var result = await h.Promote.Promote(ambition, id, new QuestionPromoteRequest(null), default);

            var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
            Assert.Equal($"comment {id} is not a question on {ambition}", bad.Value);
        }
    }

    [Fact]
    public async Task AnIssueWithNoAmbitionAbove_Is400()
    {
        var h = await NewAsync();
        var epic = await h.FileAsync("epic");
        var id = await h.AskAsync(epic, "Which way?");
        await h.AnswerAsync(epic, id, "a principle with nowhere to land");

        var result = await h.Promote.Promote(epic, id, new QuestionPromoteRequest(null), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"{epic} hangs beneath no ambition", bad.Value);
    }

    [Fact]
    public async Task AnEmptySentence_Is400()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var id = await h.AskAsync(ambition, "Which way?");
        var answerId = await h.AnswerAsync(ambition, id, "fine");

        // Whitespace-only behind the route's back: AddComment itself refuses
        // an empty body, the way QuestionLevelControllerTests strips a
        // recommendation to reach a state the route alone could not create.
        var row = await h.Db.Comments.SingleAsync(c => c.Id == answerId);
        row.Body = "   ";
        await h.Db.SaveChangesAsync();

        var result = await h.Promote.Promote(ambition, id, new QuestionPromoteRequest(null), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("a principle needs something in it", bad.Value);
    }

    [Fact]
    public async Task AnEmptySentenceGiven_Is400()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var id = await h.AskAsync(ambition, "Which way?");
        await h.AnswerAsync(ambition, id, "fine");

        var result = await h.Promote.Promote(ambition, id, new QuestionPromoteRequest("   "), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("a principle needs something in it", bad.Value);
    }

    [Fact]
    public async Task ASentenceOverTheLimit_Is400()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var id = await h.AskAsync(ambition, "Which way?");
        await h.AnswerAsync(ambition, id, "fine");

        var tooLong = new string('x', QuestionPromoteRequest.MaxPrincipleLength + 1);
        var result = await h.Promote.Promote(ambition, id, new QuestionPromoteRequest(tooLong), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal($"a principle is at most {QuestionPromoteRequest.MaxPrincipleLength} characters", bad.Value);
    }

    [Fact]
    public async Task ADuplicate_Is400_ComparedTrimmedAndWithoutCase()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        await h.SetDescriptionAsync(ambition, """
            ## Principles

            - least surprise over least typing
            """);
        var id = await h.AskAsync(ambition, "Which way?");
        await h.AnswerAsync(ambition, id, "  LEAST SURPRISE OVER LEAST TYPING  ");

        var result = await h.Promote.Promote(ambition, id, new QuestionPromoteRequest(null), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("already a principle", (string)bad.Value!);
        Assert.Empty(await h.EventsOnAsync(ambition));
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>(
            (await h.Promote.Promote("AER-404", 1, new QuestionPromoteRequest(null), default)).Result);
    }

    // ---- Keys ----

    [Fact]
    public async Task AProgramCaller_GetsA403AndNothingIsWritten()
    {
        var h = await NewAsync();
        var ambition = await h.FileAsync("ambition");
        var id = await h.AskAsync(ambition, "Which way?");
        await h.AnswerAsync(ambition, id, "a principle a key must not write");
        var before = (await h.IssueAsync(ambition)).Description;
        h.Caller.Local = new Actor(ActorKind.Key, Guid.NewGuid(), "a-runner");

        var refusal = await h.Promote.Promote(ambition, id, new QuestionPromoteRequest(null), default);

        var obj = Assert.IsType<ObjectResult>(refusal.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, obj.StatusCode);
        Assert.Equal("promoting an answer into a principle is a person's call, not an agent's", obj.Value);
        Assert.Equal(before, (await h.IssueAsync(ambition)).Description);
        Assert.Empty(await h.EventsOnAsync(ambition));
    }

    [Fact]
    public void Promote_CarriesAPersonOnlyRoleWithNoAcceptKey()
    {
        var method = typeof(QuestionPromoteController).GetMethod(nameof(QuestionPromoteController.Promote))!;
        var attribute = method.GetCustomAttribute<RequireRoleAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(PersonRole.User, attribute!.Minimum);
        Assert.False(attribute.AcceptKey);
        Assert.Null(typeof(QuestionPromoteController).GetCustomAttribute<RequireRoleAttribute>());
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required QuestionPromoteController Promote { get; init; }
        public required QuestionsController Questions { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<string> FileAsync(string type, string? parent = null) =>
            Created(await Issues.CreateIssue(
                new IssueCreateRequest(ProjectId, type, $"a {type}", "", parent, null, null), default)).Key;

        public async Task<long> AskAsync(string key, string body) =>
            Created(await Thread.AddComment(key, new CommentCreateRequest(body, "question"), default)).Id;

        public async Task<long> AnswerAsync(string key, long questionId, string body) =>
            Created(await Thread.AddComment(key, new CommentCreateRequest(body, "answer", questionId), default)).Id;

        public async Task<QuestionPromoteDto> PromoteAsync(string key, long id, string? principle = null) =>
            Value(await Promote.Promote(key, id, new QuestionPromoteRequest(principle), default));

        public async Task<IssueDto> IssueAsync(string key) => Value(await Issues.GetIssue(key, default));

        public async Task SetDescriptionAsync(string key, string description)
        {
            Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
            var issue = await Db.Issues.WithKey(projectKey, number).SingleAsync();
            issue.Description = description;
            await Db.SaveChangesAsync();
        }

        /// <summary>
        /// The <c>redescribed</c> and <c>principle_promoted</c> events a
        /// promote writes on the ambition at <paramref name="key"/>, oldest
        /// first - never the <c>created</c> or <c>asked</c>/<c>answered</c>
        /// events filing and answering already write on it.
        /// </summary>
        public async Task<IReadOnlyList<EfHatchIssueEvent>> EventsOnAsync(string key)
        {
            Assert.True(IssueKey.TryParse(key, out var projectKey, out var number));
            var id = await Db.Issues.WithKey(projectKey, number).Select(i => i.Id).SingleAsync();
            var kinds = new[] { EfHatchIssueEvent.Redescribed, EfHatchIssueEvent.PrinciplePromoted };
            return (await Db.IssueEvents.AsNoTracking()
                    .Where(e => e.IssueId == id && kinds.Contains(e.Kind))
                    .ToListAsync())
                .OrderBy(e => e.Id)
                .ToList();
        }
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
            Promote = new QuestionPromoteController(db, caller, time),
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

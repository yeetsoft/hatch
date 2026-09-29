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
/// What a runner may write about the build on a branch's tip, what the board
/// refuses to keep, and the rules a verdict is kept by: one per repository, a
/// repeat is not news, and a build that fails again on an agent's own fix
/// arrives with a question.
/// </summary>
public class BuildCheckControllerTests
{
    private const string Remote = "git@forge.example:owner/repo.git";
    private const string Sha = "2222222222222222222222222222222222222222";
    private const string NextSha = "3333333333333333333333333333333333333333";

    // ---- The four verdicts ----

    [Fact]
    public async Task AFailedVerdict_IsKept_NamingItsChecksInOrder()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Failed("b", "a")));

        Assert.Equal(BuildVerdicts.Failed, kept.Verdict);
        Assert.Equal("ha-1-thing", kept.Branch);
        Assert.Equal(Sha, kept.Sha);
        Assert.Equal(Now, kept.ShaSince);
        Assert.Equal(["a", "b"], kept.Failing.Select(f => f.Name));
        Assert.Equal("https://forge.example/checks/a", kept.Failing[0].Url);
        Assert.False(kept.PushedByIncrement);
        Assert.Equal("box:/work/repo", kept.Runner);
        Assert.Equal("Nathan", kept.CheckedBy);
        Assert.Equal(Now, kept.CheckedAt);
        Assert.Equal("forge.example/owner/repo", kept.Canonical);
    }

    [Theory]
    [InlineData(BuildVerdicts.Passed)]
    [InlineData(BuildVerdicts.None)]
    public async Task TheOtherTwo_KeepNoFailingChecks_EvenWhenTheRunnerSendsThem(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Failed("a") with { Verdict = verdict }));

        Assert.Equal(verdict, kept.Verdict);
        Assert.Empty(kept.Failing);
    }

    [Fact]
    public async Task APendingVerdict_KeepsTheChecksThatHaveAlreadyFailed()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Failed("b", "a") with { Verdict = BuildVerdicts.Pending }));

        Assert.Equal(BuildVerdicts.Pending, kept.Verdict);
        Assert.Equal(["a", "b"], kept.Failing.Select(f => f.Name));
    }

    [Fact]
    public async Task APendingVerdict_WithNothingFailedYet_IsNotRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Pending()));

        Assert.Equal(BuildVerdicts.Pending, kept.Verdict);
        Assert.Empty(kept.Failing);
    }

    // ---- Every refusal ----

    [Fact]
    public async Task ARemoteThatDoesNotCanonicalise_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Equal("a remote can't be empty", Reason(await h.PutAsync(issue, Passed() with { Remote = "  " })));
        Assert.Contains("remote", Reason(await h.PutAsync(issue, Passed() with { Remote = "nonsense" })));
        Assert.Empty(await h.Db.BuildChecks.ToListAsync());
    }

    [Theory]
    [InlineData("green")]
    [InlineData("")]
    [InlineData("PASSED")]
    public async Task AVerdictThatIsNotOneOfTheFour_IsRefused(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Equal(
            "a verdict is one of passed, failed, pending, none",
            Reason(await h.PutAsync(issue, Passed() with { Verdict = verdict })));
    }

    [Fact]
    public async Task AFailedVerdictWithNoChecks_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Equal("a failed verdict names the checks that failed", Reason(await h.PutAsync(issue, Failed())));
        Assert.Equal("a failed verdict names the checks that failed",
            Reason(await h.PutAsync(issue, Failed() with { Failing = null })));
    }

    [Fact]
    public async Task AVerdictWithNoSha_NoBranch_OrNoRunner_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Contains("sha", Reason(await h.PutAsync(issue, Passed() with { Sha = " " })));
        Assert.Contains("branch", Reason(await h.PutAsync(issue, Passed() with { Branch = "" })));
        Assert.Contains("runner", Reason(await h.PutAsync(issue, Passed() with { Runner = " " })));
        Assert.Contains("at most", Reason(await h.PutAsync(
            issue, Passed() with { Runner = new string('r', ClaimRequest.MaxRunnerLength + 1) })));
        Assert.Contains("at most", Reason(await h.PutAsync(
            issue, Passed() with { Sha = new string('a', EfHatchBuildCheck.MaxShaLength + 1) })));
        Assert.Empty(await h.Db.BuildChecks.ToListAsync());
    }

    [Fact]
    public async Task TooManyFailingChecks_AnOverlongName_AndANameWithALineBreak_AreRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var many = Enumerable.Range(0, EfHatchBuildCheck.MaxFailing + 1).Select(i => $"check {i}").ToArray();
        Assert.Equal(
            $"a verdict names at most {EfHatchBuildCheck.MaxFailing} failing checks",
            Reason(await h.PutAsync(issue, Failed(many))));

        Assert.Contains("name", Reason(await h.PutAsync(
            issue, Failed(new string('n', EfHatchBuildCheck.MaxCheckNameLength + 1)))));
        Assert.Contains("name", Reason(await h.PutAsync(issue, Failed("two\nlines"))));
        Assert.Contains("name", Reason(await h.PutAsync(issue, Failed(" "))));
    }

    [Fact]
    public async Task AnIssueThatIsNotThere_IsNotFound()
    {
        var h = await NewAsync();

        Assert.IsType<NotFoundResult>((await h.Build.PutBuildCheck("AER-404", Passed(), default)).Result);
        Assert.IsType<NotFoundResult>((await h.Build.PutBuildCheck("nonsense", Passed(), default)).Result);
    }

    [Fact]
    public async Task TheIssuesColumn_IsNotChecked()
    {
        var h = await NewAsync();

        // Filed in the inbox and never moved: an increment reports the tip it
        // pushed before anybody has moved the ticket.
        var issue = await h.FileAsync();

        Assert.Equal(BuildVerdicts.Passed, Value(await h.PutAsync(issue, Passed())).Verdict);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("ftp://forge.example/x")]
    [InlineData("not a url")]
    public async Task ACheckLinkThatIsNotAWebAddress_IsStoredAsNull_AndTheVerdictIsNotRefused(string url)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Failed() with { Failing = [new FailingCheckDto("a", url)] }));

        Assert.Equal(BuildVerdicts.Failed, kept.Verdict);
        Assert.Null(Assert.Single(kept.Failing).Url);
    }

    // ---- One verdict per issue per repository ----

    [Fact]
    public async Task ASecondPut_ReplacesTheFirst()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Pending());

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.PutAsync(issue, Failed("a"));

        var row = await h.Db.BuildChecks.SingleAsync();
        Assert.Equal(BuildVerdicts.Failed, row.Verdict);
        Assert.Equal(Now.AddMinutes(5), row.CheckedAt);
    }

    [Fact]
    public async Task TheSameRepositorySpelledTwoWays_IsOneVerdict()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue, Passed() with { Remote = "git@forge.example:owner/repo.git" });
        await h.PutAsync(issue, Passed() with { Remote = "https://forge.example/owner/repo" });

        Assert.Single(await h.Db.BuildChecks.ToListAsync());
    }

    [Fact]
    public async Task TwoRepositoriesOnOneIssue_AreKeptApart()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue, Passed());
        await h.PutAsync(issue, Failed("a") with { Remote = "git@forge.example:owner/other.git" });

        var checks = (await h.ReadAsync(issue.Key)).BuildChecks!;
        Assert.Equal(2, checks.Count);
        Assert.Equal(BuildVerdicts.Failed, checks.Single(c => c.Canonical == "forge.example/owner/other").Verdict);
        Assert.Equal(BuildVerdicts.Passed, checks.Single(c => c.Canonical == "forge.example/owner/repo").Verdict);
    }

    // ---- The sha, and the mark ----

    [Fact]
    public async Task TheSameSha_KeepsWhenTheBoardFirstHeardOfIt_AndANewOneResetsIt()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Pending());

        h.Time.Advance(TimeSpan.FromMinutes(4));
        var same = Value(await h.PutAsync(issue, Pending()));
        Assert.Equal(Now, same.ShaSince);

        var moved = Value(await h.PutAsync(issue, Pending() with { Sha = NextSha }));
        Assert.Equal(Now.AddMinutes(4), moved.ShaSince);
    }

    [Fact]
    public async Task ThePushedMark_StaysSetForTheSameSha_AndAnewShaTakesTheRequests()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Pending() with { PushedByIncrement = true });

        // A later poll on the same sha does not unmark it.
        Assert.True(Value(await h.PutAsync(issue, Pending())).PushedByIncrement);

        // Somebody else's push is not an increment's.
        Assert.False(Value(await h.PutAsync(issue, Pending() with { Sha = NextSha })).PushedByIncrement);
    }

    // ---- The trail ----

    [Fact]
    public async Task TheFirstVerdictForARepository_IsAChangeFromNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue, Failed("a"));

        var payload = Assert.Single(await h.EventsAsync(issue.Key)).Payload!.Value;
        Assert.Equal("forge.example/owner/repo", payload.GetProperty("remote").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("from").ValueKind);
        Assert.Equal("failed", payload.GetProperty("to").GetProperty("verdict").GetString());
        Assert.Equal(Sha, payload.GetProperty("to").GetProperty("sha").GetString());
        Assert.Equal(["a"], payload.GetProperty("to").GetProperty("failing").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task ADifferentVerdict_Sha_OrSetOfFailingNames_WritesAnEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Failed("a"));

        await h.PutAsync(issue, Failed("a", "b"));
        Assert.Equal(2, (await h.EventsAsync(issue.Key)).Count);

        await h.PutAsync(issue, Failed("a", "b") with { Sha = NextSha });
        Assert.Equal(3, (await h.EventsAsync(issue.Key)).Count);

        await h.PutAsync(issue, Passed() with { Sha = NextSha });
        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(4, events.Count);
        Assert.Equal("failed", events[3].Payload!.Value.GetProperty("from").GetProperty("verdict").GetString());
        Assert.Equal("passed", events[3].Payload!.Value.GetProperty("to").GetProperty("verdict").GetString());
    }

    [Fact]
    public async Task AVerdictThatRepeatsTheStoredOne_WritesNoEvent_ButRefreshesTheRest()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Failed("a", "b"));

        h.Time.Advance(TimeSpan.FromMinutes(10));
        h.Caller.Person = new EfPerson { Name = "Other", CreatedAt = Now, UpdatedAt = Now };

        // The same checks in another order, from another runner.
        await h.PutAsync(issue, Failed("b", "a") with { Runner = "elsewhere:/work/repo" });

        Assert.Single(await h.EventsAsync(issue.Key));

        var row = await h.Db.BuildChecks.SingleAsync();
        Assert.Equal(Now.AddMinutes(10), row.CheckedAt);
        Assert.Equal("elsewhere:/work/repo", row.Runner);
        Assert.Equal("Other", row.CheckedBy);
    }

    [Fact]
    public async Task TheMarkAlone_WritesNoEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Pending());

        await h.PutAsync(issue, Pending() with { PushedByIncrement = true });

        Assert.Single(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task AVerdict_DoesNotMoveTheIssue()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var was = await h.ReadAsync(issue.Key);

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.PutAsync(issue, Failed("a"));

        var now = await h.ReadAsync(issue.Key);
        Assert.Equal(was.UpdatedAt, now.UpdatedAt);
        Assert.Equal(was.StatusId, now.StatusId);
    }

    // ---- A mark never lowers a concluded verdict ----

    [Theory]
    [InlineData(BuildVerdicts.Passed)]
    [InlineData(BuildVerdicts.Failed)]
    public async Task AMarkOfPending_OverAConcludedVerdictOnTheSameSha_KeepsTheVerdict_AndTakesTheFlag(string stored)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, stored == BuildVerdicts.Failed ? Failed("a") : Passed());

        var kept = Value(await h.PutAsync(issue, Pending() with { PushedByIncrement = true }));

        Assert.Equal(stored, kept.Verdict);
        Assert.True(kept.PushedByIncrement);
        Assert.Equal(stored == BuildVerdicts.Failed ? ["a"] : [], kept.Failing.Select(f => f.Name));
    }

    [Fact]
    public async Task APollsOwnPending_OverAFailedVerdictOnTheSameSha_IsStored()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Failed("a"));

        // A re-run: the build is running again.
        var kept = Value(await h.PutAsync(issue, Pending()));

        Assert.Equal(BuildVerdicts.Pending, kept.Verdict);
        Assert.Empty(kept.Failing);
    }

    // ---- The failed-again question ----

    [Fact]
    public async Task AFailedVerdictOnAShaAnIncrementPushed_OpensOneQuestion_WithTheSharedOptions()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Pending() with { PushedByIncrement = true });
        Assert.Empty(await h.QuestionsAsync());

        await h.PutAsync(issue, Failed("api", "CI"));

        var question = Assert.Single(await h.QuestionsAsync());
        Assert.Equal("Nathan", question.Author);
        Assert.Contains(Sha[..10], question.Body);
        Assert.Contains("- api", question.Body);
        Assert.Contains("- CI", question.Body);
        Assert.Contains("build increment", question.Body);
        Assert.Equal(
            StallAnswers.Options().Select(o => (o.Label, o.Detail, o.Recommended)),
            Questions.ReadOptions(question.Options)!.Select(o => (o.Label, o.Detail, o.Recommended)));
        Assert.Single(await h.Db.IssueEvents.Where(e => e.Kind == EfHatchIssueEvent.Asked).ToListAsync());

        // Again: no second one.
        await h.PutAsync(issue, Failed("api", "CI"));
        Assert.Single(await h.QuestionsAsync());
    }

    [Fact]
    public async Task AFailedVerdictOnAShaNoIncrementPushed_OpensNoQuestion()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue, Failed("a"));

        Assert.Empty(await h.QuestionsAsync());
    }

    [Fact]
    public async Task TheMarkArrivingAfterTheFailure_OpensTheQuestionAllTheSame()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        // Another runner's poll read the build between the push and the mark.
        await h.PutAsync(issue, Failed("a"));
        Assert.Empty(await h.QuestionsAsync());

        var kept = Value(await h.PutAsync(issue, Pending() with { PushedByIncrement = true }));

        Assert.Equal(BuildVerdicts.Failed, kept.Verdict);
        Assert.True(kept.PushedByIncrement);
        Assert.Single(await h.QuestionsAsync());
    }

    [Fact]
    public async Task AQuestionAlreadyOpen_IsNotAskedAgain()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        Value(await h.Thread.AddComment(issue.Key, new CommentCreateRequest("Which way?", "question"), default));
        await h.PutAsync(issue, Pending() with { PushedByIncrement = true });

        await h.PutAsync(issue, Failed("a"));

        Assert.Single(await h.QuestionsAsync());
    }

    [Fact]
    public async Task AFailureOnANewShaThatSomebodyElsePushed_OpensNoQuestion()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Pending() with { PushedByIncrement = true });

        // The tip moved: the agent's fix is not what is failing now.
        await h.PutAsync(issue, Failed("a") with { Sha = NextSha });

        Assert.Empty(await h.QuestionsAsync());
    }

    // ---- What every issue carries ----

    [Fact]
    public async Task AnIssueWithNoVerdict_CarriesAnEmptyList()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Empty((await h.ReadAsync(issue.Key)).BuildChecks!);
    }

    [Fact]
    public async Task AListOfIssues_ReadsItsVerdictsInOneBatch()
    {
        var h = await NewAsync();
        var one = await h.FileAsync();
        var two = await h.FileAsync();
        var three = await h.FileAsync();
        await h.PutAsync(one, Passed());
        await h.PutAsync(two, Failed("a"));

        var dtos = await IssueProjection.ToDtosAsync(
            h.Db, new StubActorDirectory(),
            await h.Db.Issues.OrderBy(i => i.Number).ToListAsync(),
            TestClaims.With(), Now, default);

        var byKey = dtos.Values.ToDictionary(d => d.Key);
        Assert.Equal(BuildVerdicts.Passed, byKey[one.Key].BuildChecks!.Single().Verdict);
        Assert.Equal(BuildVerdicts.Failed, byKey[two.Key].BuildChecks!.Single().Verdict);
        Assert.Empty(byKey[three.Key].BuildChecks!);
    }

    [Fact]
    public async Task DeletingTheIssue_TakesItsVerdicts()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Passed());

        h.Db.Issues.Remove(await h.Db.Issues.SingleAsync());
        await h.Db.SaveChangesAsync();

        Assert.Empty(await h.Db.BuildChecks.ToListAsync());
    }

    // ---- The gate ----

    [Fact]
    public void WritingOne_IsOpenToAHatchScopedKey()
    {
        var guard = typeof(BuildCheckController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static BuildCheckRequest Passed() =>
        new(Remote, "ha-1-thing", Sha, BuildVerdicts.Passed, null, "box:/work/repo");

    private static BuildCheckRequest Pending() => Passed() with { Verdict = BuildVerdicts.Pending };

    private static BuildCheckRequest Failed(params string[] names) =>
        Passed() with
        {
            Verdict = BuildVerdicts.Failed,
            Failing = names.Select(n => new FailingCheckDto(n, $"https://forge.example/checks/{n}")).ToList(),
        };

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required BuildCheckController Build { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
        public required MergeCheckControllerTests.StubCallerIdentity Caller { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public async Task<IssueDto> FileAsync() =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", "a thing", null, null, null, null), default));

        public Task<ActionResult<BuildCheckDto>> PutAsync(IssueDto issue, BuildCheckRequest request) =>
            Build.PutBuildCheck(issue.Key, request, default);

        public async Task<IssueDto> ReadAsync(string key) => Value(await Issues.GetIssue(key, default));

        /// <summary>The verdict's own events, oldest first.</summary>
        public async Task<IReadOnlyList<IssueEventDto>> EventsAsync(string key) =>
            Value(await Thread.GetEvents(key, default))
                .Where(e => e.Kind == EfHatchIssueEvent.BuildCheckChanged)
                .Reverse()
                .ToList();

        /// <summary>Every question on the board.</summary>
        public Task<List<EfHatchComment>> QuestionsAsync() =>
            Db.Comments.Where(c => c.Kind == EfHatchComment.Question).ToListAsync();
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        db.Add(new EfHatchStatus { Name = "inbox", SortOrder = 10 });
        db.Add(new EfHatchStatus { Name = "done", SortOrder = 20, IsTerminal = true });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new MergeCheckControllerTests.StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Build = new BuildCheckController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time),
            Thread = new IssueThreadController(db, caller, time),
            Caller = caller,
            Time = time,
            ProjectId = hatch.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {ReasonOf(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {ReasonOf(result.Result)}");

    /// <summary>The plain-text reason on a refusal, and a failure if it was not one.</summary>
    private static string Reason(ActionResult<BuildCheckDto> result) => ReasonOf(result.Result);

    private static string ReasonOf(IActionResult? result) => result switch
    {
        BadRequestObjectResult o => o.Value?.ToString() ?? "",
        var other => throw new InvalidOperationException($"expected a refusal, got {other?.GetType().Name ?? "a value"}"),
    };
}

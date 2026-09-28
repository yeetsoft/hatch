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
/// What a runner may write about whether a build passed, what the board
/// refuses to keep, the rules a verdict is kept by - one per repository, and a
/// repeat is not news - and when a failed build becomes a question.
/// </summary>
public class BuildCheckControllerTests
{
    private const string Remote = "git@forge.example:owner/repo.git";
    private const string Sha = "2222222222222222222222222222222222222222";
    private const string OtherSha = "3333333333333333333333333333333333333333";

    // ---- The four verdicts ----

    [Fact]
    public async Task APassedVerdict_IsKept()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Passed()));

        Assert.Equal(BuildVerdicts.Passed, kept.Verdict);
        Assert.Equal("ha-1-thing", kept.Branch);
        Assert.Equal(Sha, kept.Sha);
        Assert.Empty(kept.Failing);
        Assert.Equal("box:/work/repo", kept.Runner);
        Assert.Equal(Now, kept.CheckedAt);
        Assert.Equal(Now, kept.ShaSince);
        Assert.False(kept.PushedByIncrement);
        Assert.Equal(Remote, kept.Remote);
        Assert.Equal("forge.example/owner/repo", kept.Canonical);
    }

    [Fact]
    public async Task AFailedVerdict_NamesItsChecks_SortedAndWithoutRepeats()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Failed(
            new FailingCheckDto("test", "https://ci.example/2"),
            new FailingCheckDto("build", "https://ci.example/1"),
            new FailingCheckDto("test", null))));

        Assert.Equal(["build", "test"], kept.Failing.Select(f => f.Name));
        Assert.Equal("https://ci.example/2", kept.Failing[1].Url);
    }

    [Theory]
    [InlineData(BuildVerdicts.Pending)]
    [InlineData(BuildVerdicts.None)]
    [InlineData(BuildVerdicts.Passed)]
    public async Task FailingChecksOnAVerdictThatIsNotFailed_AreIgnored(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Failed(Check("build")) with { Verdict = verdict }));

        Assert.Equal(verdict, kept.Verdict);
        Assert.Empty(kept.Failing);
    }

    [Fact]
    public async Task AFailingChecksUrlThatIsNotAWebAddress_IsStoredAsNothing_AndTheVerdictStands()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var kept = Value(await h.PutAsync(issue, Failed(
            new FailingCheckDto("a", "javascript:alert(1)"),
            new FailingCheckDto("b", "/relative/path"),
            new FailingCheckDto("c", "  https://ci.example/c  "),
            new FailingCheckDto("d", "http://ci.example/d"))));

        Assert.Equal(
            [null, null, "https://ci.example/c", "http://ci.example/d"],
            kept.Failing.Select(f => f.Url));
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

        var reason = Reason(await h.PutAsync(issue, Passed() with { Verdict = verdict }));

        Assert.Equal("a verdict is one of passed, failed, pending, none", reason);
    }

    [Theory]
    [MemberData(nameof(NoChecks))]
    public async Task AFailedVerdictWithNoChecks_IsRefused(IReadOnlyList<FailingCheckDto>? failing)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var reason = Reason(await h.PutAsync(issue, Failed() with { Failing = failing }));

        Assert.Equal("a failed verdict names the checks that failed", reason);
    }

    public static TheoryData<IReadOnlyList<FailingCheckDto>?> NoChecks => new()
    {
        null,
        Array.Empty<FailingCheckDto>(),
    };

    [Fact]
    public async Task AVerdictWithNoSha_NoBranch_OrNoRunner_IsRefused()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        Assert.Contains("sha", Reason(await h.PutAsync(issue, Passed() with { Sha = " " })));
        Assert.Contains("branch", Reason(await h.PutAsync(issue, Passed() with { Branch = "" })));
        Assert.Contains("branch", Reason(await h.PutAsync(issue, Passed() with { Branch = null! })));
        Assert.Contains("runner", Reason(await h.PutAsync(issue, Passed() with { Runner = " " })));
        Assert.Contains("at most", Reason(await h.PutAsync(
            issue, Passed() with { Runner = new string('r', ClaimRequest.MaxRunnerLength + 1) })));
        Assert.Contains("at most", Reason(await h.PutAsync(
            issue, Passed() with { Sha = new string('a', EfHatchMergeCheck.MaxShaLength + 1) })));
        Assert.Empty(await h.Db.BuildChecks.ToListAsync());
    }

    [Fact]
    public async Task TooManyChecks_AnOverlongName_AndABreakInAName_AreEachRefusedInASentence()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var many = Enumerable.Range(0, EfHatchBuildCheck.MaxFailing + 1).Select(n => Check($"check-{n}")).ToArray();
        Assert.Equal("a verdict names at most 100 failing checks", Reason(await h.PutAsync(issue, Failed(many))));

        Assert.Contains("at most 200", Reason(await h.PutAsync(
            issue, Failed(Check(new string('n', EfHatchBuildCheck.MaxNameLength + 1))))));
        Assert.Contains("line break", Reason(await h.PutAsync(issue, Failed(Check("a\nb")))));
        Assert.Contains("name", Reason(await h.PutAsync(issue, Failed(Check(" ")))));
        Assert.Contains("url is at most", Reason(await h.PutAsync(
            issue, Failed(new FailingCheckDto("a", "https://ci.example/" + new string('x', EfHatchBuildCheck.MaxUrlLength))))));
        Assert.Empty(await h.Db.BuildChecks.ToListAsync());
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
        var issue = await h.FileAsync();

        Assert.Equal(BuildVerdicts.Passed, Value(await h.PutAsync(issue, Passed())).Verdict);
    }

    // ---- One verdict per issue per repository ----

    [Fact]
    public async Task ASecondPut_ReplacesTheFirst()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Passed());

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.PutAsync(issue, Failed(Check("build")) with { Sha = OtherSha });

        var row = await h.Db.BuildChecks.SingleAsync();
        Assert.Equal(BuildVerdicts.Failed, row.Verdict);
        Assert.Equal(OtherSha, row.Sha);
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
        await h.PutAsync(issue, Failed(Check("build")) with { Remote = "git@forge.example:owner/other.git" });

        var checks = (await h.ReadAsync(issue.Key)).BuildChecks!;
        Assert.Equal(2, checks.Count);
        Assert.Equal(BuildVerdicts.Failed, checks.Single(c => c.Canonical == "forge.example/owner/other").Verdict);
        Assert.Equal(BuildVerdicts.Passed, checks.Single(c => c.Canonical == "forge.example/owner/repo").Verdict);
        Assert.Equal(2, (await h.EventsAsync(issue.Key)).Count);
    }

    // ---- The trail ----

    [Fact]
    public async Task AVerdictThatChangesTheStoredOne_WritesAnEventCarryingBothSides()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Passed());

        await h.PutAsync(issue, Failed(Check("test"), Check("build")));

        var events = await h.EventsAsync(issue.Key);
        Assert.Equal(2, events.Count);

        var payload = events[1].Payload!.Value;
        Assert.Equal("Nathan", events[1].Actor);
        Assert.Equal("forge.example/owner/repo", payload.GetProperty("remote").GetString());
        Assert.Equal(Sha, payload.GetProperty("sha").GetString());
        Assert.Equal("passed", payload.GetProperty("from").GetProperty("verdict").GetString());
        Assert.Equal("failed", payload.GetProperty("to").GetProperty("verdict").GetString());
        Assert.Equal(["build", "test"], payload.GetProperty("to").GetProperty("failing").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public async Task TheFirstVerdictForARepository_IsAChangeFromNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue, Passed());

        var payload = Assert.Single(await h.EventsAsync(issue.Key)).Payload!.Value;
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("from").ValueKind);
    }

    [Fact]
    public async Task ADifferentShaOrSetOfNames_IsAChangeEvenWhenTheVerdictIsNot()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Failed(Check("a")));

        await h.PutAsync(issue, Failed(Check("a"), Check("b")));
        Assert.Equal(2, (await h.EventsAsync(issue.Key)).Count);

        await h.PutAsync(issue, Failed(Check("a"), Check("b")) with { Sha = OtherSha });
        Assert.Equal(3, (await h.EventsAsync(issue.Key)).Count);
    }

    [Fact]
    public async Task AVerdictThatRepeatsTheStoredOne_WritesNoEvent_ButRefreshesTheRest()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Failed(Check("a"), Check("b")));

        h.Time.Advance(TimeSpan.FromMinutes(10));

        // The same names in another order, from another runner.
        await h.PutAsync(issue, Failed(Check("b"), Check("a")) with { Runner = "elsewhere:/work/repo" });

        Assert.Single(await h.EventsAsync(issue.Key));

        var row = await h.Db.BuildChecks.SingleAsync();
        Assert.Equal(Now.AddMinutes(10), row.CheckedAt);
        Assert.Equal("elsewhere:/work/repo", row.Runner);
    }

    [Fact]
    public async Task AVerdict_DoesNotMoveTheIssue()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var was = await h.ReadAsync(issue.Key);

        h.Time.Advance(TimeSpan.FromHours(1));
        await h.PutAsync(issue, Failed(Check("a")));

        var now = await h.ReadAsync(issue.Key);
        Assert.Equal(was.UpdatedAt, now.UpdatedAt);
        Assert.Equal(was.StatusId, now.StatusId);
    }

    // ---- The sha, and the mark ----

    [Fact]
    public async Task TheSameSha_KeepsShaSince_AndKeepsTheMarkWhenALaterPutSendsFalse()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Pending(pushed: true));

        h.Time.Advance(TimeSpan.FromMinutes(5));
        var kept = Value(await h.PutAsync(issue, Pending(pushed: false)));

        Assert.Equal(Now, kept.ShaSince);
        Assert.Equal(Now.AddMinutes(5), kept.CheckedAt);
        Assert.True(kept.PushedByIncrement);
    }

    [Fact]
    public async Task ANewSha_ResetsShaSince_AndTakesTheRequestsMark()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Pending(pushed: true));

        h.Time.Advance(TimeSpan.FromMinutes(5));
        var kept = Value(await h.PutAsync(issue, Pending(pushed: false) with { Sha = OtherSha }));

        Assert.Equal(Now.AddMinutes(5), kept.ShaSince);
        Assert.False(kept.PushedByIncrement);
    }

    [Fact]
    public async Task TheMarkAlone_WritesNoEvent()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Pending());

        await h.PutAsync(issue, Pending(pushed: true));

        Assert.Single(await h.EventsAsync(issue.Key));
    }

    [Theory]
    [InlineData(BuildVerdicts.Passed)]
    [InlineData(BuildVerdicts.Failed)]
    public async Task AMarkOfPending_NeverLowersAConcludedVerdictOnTheSameSha(string stored)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Passed() with { Verdict = stored, Failing = [Check("build")] });

        var kept = Value(await h.PutAsync(issue, Pending(pushed: true)));

        Assert.Equal(stored, kept.Verdict);
        Assert.True(kept.PushedByIncrement);
        Assert.Equal(stored == BuildVerdicts.Failed ? new[] { "build" } : Array.Empty<string>(), kept.Failing.Select(f => f.Name));
        Assert.Single(await h.EventsAsync(issue.Key));
    }

    [Fact]
    public async Task APollsOwnPending_AfterAFailed_IsAReRunAndIsStored()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Failed(Check("build")));

        var kept = Value(await h.PutAsync(issue, Pending()));

        Assert.Equal(BuildVerdicts.Pending, kept.Verdict);
        Assert.Empty(kept.Failing);
        Assert.Equal(2, (await h.EventsAsync(issue.Key)).Count);
    }

    // ---- The failed-again question ----

    [Fact]
    public async Task PendingPushedByAnIncrement_ThenFailedOnTheSameSha_AsksOnce()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Pending(pushed: true));
        Assert.Empty(await h.QuestionsAsync());

        await h.PutAsync(issue, Failed(Check("test"), Check("build")));

        var question = Assert.Single(await h.QuestionsAsync());
        Assert.Equal(EfHatchComment.Question, question.Kind);
        Assert.Equal("Nathan", question.Author);
        Assert.Contains(Sha, question.Body);
        Assert.Contains("build, test", question.Body);
        Assert.Contains("build increment pushed", question.Body);
        Assert.Equal(
            StallQuestion.Options.Select(o => (o.Label, o.Detail, o.Recommended)),
            Questions.ReadOptions(question.Options)!.Select(o => (o.Label, o.Detail, o.Recommended)));
        Assert.Equal(1, await h.Db.IssueEvents.CountAsync(e => e.Kind == EfHatchIssueEvent.Asked));

        // Failed again: the row was already failed and marked.
        await h.PutAsync(issue, Failed(Check("test"), Check("build")));
        await h.PutAsync(issue, Failed(Check("test")));
        Assert.Single(await h.QuestionsAsync());
    }

    [Fact]
    public async Task AFailedVerdict_OnAShaNoIncrementPushed_AsksNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        await h.PutAsync(issue, Pending());
        await h.PutAsync(issue, Failed(Check("build")));

        Assert.Empty(await h.QuestionsAsync());
    }

    [Fact]
    public async Task AFailedVerdict_OnANewShaAnIncrementPushed_AsksAgain()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Failed(Check("build"), pushed: true));
        await h.AnswerAllAsync();

        await h.PutAsync(issue, Failed(Check("build"), pushed: true) with { Sha = OtherSha });

        Assert.Equal(2, (await h.QuestionsAsync()).Count);
    }

    [Fact]
    public async Task TheMarkArrivingAfterAPollsFailed_LeavesItFailed_AndAsksOnce()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        await h.PutAsync(issue, Failed(Check("build")));
        Assert.Empty(await h.QuestionsAsync());

        var kept = Value(await h.PutAsync(issue, Pending(pushed: true)));

        Assert.Equal(BuildVerdicts.Failed, kept.Verdict);
        Assert.True(kept.PushedByIncrement);
        Assert.Single(await h.QuestionsAsync());

        await h.PutAsync(issue, Pending(pushed: true));
        Assert.Single(await h.QuestionsAsync());
    }

    [Fact]
    public async Task AnOpenQuestion_IsAlreadyTheFlag_SoNoSecondIsAsked()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        h.Db.Comments.Add(new EfHatchComment
        {
            IssueId = await h.Db.Issues.Select(i => i.Id).SingleAsync(),
            Author = "someone",
            Body = "already waiting",
            Kind = EfHatchComment.Question,
            CreatedAt = Now,
        });
        await h.Db.SaveChangesAsync();

        await h.PutAsync(issue, Failed(Check("build"), pushed: true));

        Assert.Single(await h.QuestionsAsync());
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
        await h.PutAsync(two, Failed(Check("build")));

        var dtos = await IssueProjection.ToDtosAsync(
            h.Db, new StubActorDirectory(),
            await h.Db.Issues.OrderBy(i => i.Number).ToListAsync(),
            TestClaims.With(), Now, default);

        var byKey = dtos.Values.ToDictionary(d => d.Key);
        Assert.Equal(BuildVerdicts.Passed, byKey[one.Key].BuildChecks!.Single().Verdict);
        Assert.Equal("build", byKey[two.Key].BuildChecks!.Single().Failing.Single().Name);
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

    private static FailingCheckDto Check(string name) => new(name, null);

    private static BuildCheckRequest Passed() =>
        new(Remote, "ha-1-thing", Sha, BuildVerdicts.Passed, null, "box:/work/repo");

    private static BuildCheckRequest Pending(bool pushed = false) =>
        Passed() with { Verdict = BuildVerdicts.Pending, PushedByIncrement = pushed };

    private static BuildCheckRequest Failed(params FailingCheckDto[] failing) =>
        Passed() with { Verdict = BuildVerdicts.Failed, Failing = failing };

    private static BuildCheckRequest Failed(FailingCheckDto check, bool pushed) =>
        Failed(check) with { PushedByIncrement = pushed };

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required BuildCheckController Build { get; init; }
        public required IssuesController Issues { get; init; }
        public required IssueThreadController Thread { get; init; }
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

        /// <summary>Every question on the board, oldest first.</summary>
        public Task<List<EfHatchComment>> QuestionsAsync() =>
            Db.Comments.Where(c => c.Kind == EfHatchComment.Question).OrderBy(c => c.Id).ToListAsync();

        /// <summary>Answers every question there is, so that none is open.</summary>
        public async Task AnswerAllAsync()
        {
            foreach (var q in await QuestionsAsync())
                Db.Comments.Add(new EfHatchComment
                {
                    IssueId = q.IssueId, Author = "operator", Body = "leave it",
                    Kind = EfHatchComment.Answer, AnswersId = q.Id, CreatedAt = Now,
                });

            await Db.SaveChangesAsync();
        }
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
        var caller = new MergeCheckControllerTests.StubCallerIdentity
        {
            Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now },
        };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Build = new BuildCheckController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time),
            Thread = new IssueThreadController(db, caller, time),
            Time = time,
            ProjectId = hatch.Id,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {Reason(result.Result)}");

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {Reason(result.Result)}");

    /// <summary>The plain-text reason on a refusal, and a failure if it was not one.</summary>
    private static string Reason(ActionResult<BuildCheckDto> result) => result.Result switch
    {
        BadRequestObjectResult o => o.Value?.ToString() ?? "",
        var other => throw new InvalidOperationException($"expected a refusal, got {other?.GetType().Name ?? "a value"}"),
    };
}

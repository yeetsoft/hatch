using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a runner may write about the build on a repository's trunk, kept one
/// per repository per trunk rather than per issue - and the one rule that is
/// this route's own: a passed verdict lets the attached bug go.
/// </summary>
public class TrunkBuildControllerTests
{
    private const string Remote = "git@forge.example:owner/repo.git";
    private const string Sha = "2222222222222222222222222222222222222222";
    private const string NextSha = "3333333333333333333333333333333333333333";

    // ---- The four verdicts ----

    [Fact]
    public async Task AFailedVerdict_IsKept_NamingItsChecksInOrder()
    {
        var h = await NewAsync();

        var kept = Value(await h.PutAsync(Failed("b", "a")));

        Assert.Equal(BuildVerdicts.Failed, kept.Verdict);
        Assert.Equal("main", kept.Trunk);
        Assert.Equal(Sha, kept.Sha);
        Assert.Equal(Now, kept.ShaSince);
        Assert.Equal(["a", "b"], kept.Failing.Select(f => f.Name));
        Assert.Equal("https://forge.example/checks/a", kept.Failing[0].Url);
        Assert.Equal("box:/work/repo", kept.Runner);
        Assert.Equal("Nathan", kept.CheckedBy);
        Assert.Equal(Now, kept.CheckedAt);
        Assert.Equal("forge.example/owner/repo", kept.Canonical);
        Assert.Null(kept.BugIssueKey);
    }

    [Theory]
    [InlineData(BuildVerdicts.Passed)]
    [InlineData(BuildVerdicts.None)]
    public async Task TheOtherTwo_KeepNoFailingChecks_EvenWhenTheRunnerSendsThem(string verdict)
    {
        var h = await NewAsync();

        var kept = Value(await h.PutAsync(Failed("a") with { Verdict = verdict }));

        Assert.Equal(verdict, kept.Verdict);
        Assert.Empty(kept.Failing);
    }

    [Fact]
    public async Task APendingVerdict_KeepsTheChecksThatHaveAlreadyFailed()
    {
        var h = await NewAsync();

        var kept = Value(await h.PutAsync(Failed("b", "a") with { Verdict = BuildVerdicts.Pending }));

        Assert.Equal(BuildVerdicts.Pending, kept.Verdict);
        Assert.Equal(["a", "b"], kept.Failing.Select(f => f.Name));
    }

    // ---- Every refusal ----

    [Fact]
    public async Task ARemoteThatDoesNotCanonicalise_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal("a remote can't be empty", Reason(await h.PutAsync(Passed() with { Remote = "  " })));
        Assert.Contains("remote", Reason(await h.PutAsync(Passed() with { Remote = "nonsense" })));
        Assert.Empty(await h.Db.TrunkBuilds.ToListAsync());
    }

    [Theory]
    [InlineData("green")]
    [InlineData("")]
    [InlineData("PASSED")]
    public async Task AVerdictThatIsNotOneOfTheFour_IsRefused(string verdict)
    {
        var h = await NewAsync();

        Assert.Equal(
            "a verdict is one of passed, failed, pending, none",
            Reason(await h.PutAsync(Passed() with { Verdict = verdict })));
    }

    [Fact]
    public async Task AFailedVerdictWithNoChecks_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal("a failed verdict names the checks that failed", Reason(await h.PutAsync(Failed())));
        Assert.Equal("a failed verdict names the checks that failed", Reason(await h.PutAsync(Failed() with { Failing = null })));
    }

    [Fact]
    public async Task AVerdictWithNoSha_NoTrunk_OrNoRunner_IsRefused()
    {
        var h = await NewAsync();

        Assert.Contains("sha", Reason(await h.PutAsync(Passed() with { Sha = " " })));
        Assert.Contains("trunk", Reason(await h.PutAsync(Passed() with { Trunk = "" })));
        Assert.Contains("runner", Reason(await h.PutAsync(Passed() with { Runner = " " })));
        Assert.Contains("at most", Reason(await h.PutAsync(
            Passed() with { Runner = new string('r', ClaimRequest.MaxRunnerLength + 1) })));
        Assert.Contains("at most", Reason(await h.PutAsync(
            Passed() with { Sha = new string('a', EfHatchTrunkBuild.MaxShaLength + 1) })));
        Assert.Empty(await h.Db.TrunkBuilds.ToListAsync());
    }

    [Fact]
    public async Task TooManyFailingChecks_AnOverlongName_AndANameWithALineBreak_AreRefused()
    {
        var h = await NewAsync();

        var many = Enumerable.Range(0, EfHatchTrunkBuild.MaxFailing + 1).Select(i => $"check {i}").ToArray();
        Assert.Equal(
            $"a verdict names at most {EfHatchTrunkBuild.MaxFailing} failing checks",
            Reason(await h.PutAsync(Failed(many))));

        Assert.Contains("name", Reason(await h.PutAsync(Failed(new string('n', EfHatchTrunkBuild.MaxCheckNameLength + 1)))));
        Assert.Contains("name", Reason(await h.PutAsync(Failed("two\nlines"))));
        Assert.Contains("name", Reason(await h.PutAsync(Failed(" "))));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("ftp://forge.example/x")]
    [InlineData("not a url")]
    public async Task ACheckLinkThatIsNotAWebAddress_IsStoredAsNull_AndTheVerdictIsNotRefused(string url)
    {
        var h = await NewAsync();

        var kept = Value(await h.PutAsync(Failed() with { Failing = [new FailingCheckDto("a", url)] }));

        Assert.Equal(BuildVerdicts.Failed, kept.Verdict);
        Assert.Null(Assert.Single(kept.Failing).Url);
    }

    // ---- One verdict per repository per trunk ----

    [Fact]
    public async Task ASecondPut_ReplacesTheFirst()
    {
        var h = await NewAsync();
        await h.PutAsync(Pending());

        h.Time.Advance(TimeSpan.FromMinutes(5));
        await h.PutAsync(Failed("a"));

        var row = await h.Db.TrunkBuilds.SingleAsync();
        Assert.Equal(BuildVerdicts.Failed, row.Verdict);
        Assert.Equal(Now.AddMinutes(5), row.CheckedAt);
    }

    [Fact]
    public async Task TheSameRepositorySpelledTwoWays_IsOneVerdict()
    {
        var h = await NewAsync();

        await h.PutAsync(Passed() with { Remote = "git@forge.example:owner/repo.git" });
        await h.PutAsync(Passed() with { Remote = "https://forge.example/owner/repo" });

        Assert.Single(await h.Db.TrunkBuilds.ToListAsync());
    }

    [Fact]
    public async Task TwoTrunksOnOneRepository_AreKeptApart()
    {
        var h = await NewAsync();

        await h.PutAsync(Passed());
        await h.PutAsync(Failed("a") with { Trunk = "release" });

        var rows = await h.Db.TrunkBuilds.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(BuildVerdicts.Failed, rows.Single(r => r.Trunk == "release").Verdict);
        Assert.Equal(BuildVerdicts.Passed, rows.Single(r => r.Trunk == "main").Verdict);
    }

    [Fact]
    public async Task TwoRepositoriesOnTheSameTrunkName_AreKeptApart()
    {
        var h = await NewAsync();

        await h.PutAsync(Passed());
        await h.PutAsync(Failed("a") with { Remote = "git@forge.example:owner/other.git" });

        var rows = await h.Db.TrunkBuilds.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(BuildVerdicts.Failed, rows.Single(r => r.Canonical == "forge.example/owner/other").Verdict);
        Assert.Equal(BuildVerdicts.Passed, rows.Single(r => r.Canonical == "forge.example/owner/repo").Verdict);
    }

    // ---- The sha ----

    [Fact]
    public async Task TheSameSha_KeepsWhenTheBoardFirstHeardOfIt_AndANewOneResetsIt()
    {
        var h = await NewAsync();
        await h.PutAsync(Pending());

        h.Time.Advance(TimeSpan.FromMinutes(4));
        var same = Value(await h.PutAsync(Pending()));
        Assert.Equal(Now, same.ShaSince);

        var moved = Value(await h.PutAsync(Pending() with { Sha = NextSha }));
        Assert.Equal(Now.AddMinutes(4), moved.ShaSince);
    }

    // ---- No event, and no question ----

    [Fact]
    public async Task NothingHereWritesAnEventOrAQuestion_ThereIsNoIssueToWriteOneOn()
    {
        var h = await NewAsync();

        await h.PutAsync(Failed("a"));
        await h.PutAsync(Passed());

        Assert.Empty(await h.Db.IssueEvents.ToListAsync());
        Assert.Empty(await h.Db.Comments.Where(c => c.Kind == EfHatchComment.Question).ToListAsync());
    }

    // ---- The bug stays attached until the trunk passes ----

    [Fact]
    public async Task APassedVerdict_LetsTheAttachedBugGo()
    {
        var h = await NewAsync();
        await h.PutAsync(Failed("a"));
        var bug = await h.FileBugAsync();
        var row = await h.Db.TrunkBuilds.SingleAsync();
        row.BugIssueId = bug.Id;
        await h.Db.SaveChangesAsync();

        var kept = Value(await h.PutAsync(Failed("a") with { Sha = NextSha }));
        Assert.Equal(bug.Key, kept.BugIssueKey);

        var passed = Value(await h.PutAsync(Passed() with { Sha = NextSha }));
        Assert.Null(passed.BugIssueKey);
        Assert.Null((await h.Db.TrunkBuilds.SingleAsync()).BugIssueId);
    }

    [Fact]
    public async Task AFailingBuildOnANewSha_KeepsTheAttachedBug()
    {
        var h = await NewAsync();
        await h.PutAsync(Failed("a"));
        var bug = await h.FileBugAsync();
        var row = await h.Db.TrunkBuilds.SingleAsync();
        row.BugIssueId = bug.Id;
        await h.Db.SaveChangesAsync();

        // A fix that fails again is the same outage.
        var kept = Value(await h.PutAsync(Failed("b") with { Sha = NextSha }));

        Assert.Equal(bug.Key, kept.BugIssueKey);
    }

    // ---- Reading ----

    [Fact]
    public async Task GetTrunkBuilds_AnswersEveryStoredVerdict_OrderedByCanonicalThenTrunk()
    {
        var h = await NewAsync();
        await h.PutAsync(Passed() with { Remote = "git@forge.example:owner/repo.git", Trunk = "release" });
        await h.PutAsync(Passed() with { Remote = "git@forge.example:owner/repo.git", Trunk = "main" });
        await h.PutAsync(Passed() with { Remote = "git@forge.example:owner/another.git" });

        var rows = Value(await h.Build.GetTrunkBuilds(default));

        Assert.Equal(
            [("forge.example/owner/another", "main"), ("forge.example/owner/repo", "main"), ("forge.example/owner/repo", "release")],
            rows.Select(r => (r.Canonical, r.Trunk)));
    }

    [Fact]
    public async Task GetTrunkBuilds_CarriesTheAttachedBugsKey()
    {
        var h = await NewAsync();
        await h.PutAsync(Failed("a"));
        var bug = await h.FileBugAsync();
        var row = await h.Db.TrunkBuilds.SingleAsync();
        row.BugIssueId = bug.Id;
        await h.Db.SaveChangesAsync();

        var rows = Value(await h.Build.GetTrunkBuilds(default));

        Assert.Equal(bug.Key, Assert.Single(rows).BugIssueKey);
    }

    // ---- The gate ----

    [Fact]
    public void WritingOne_IsOpenToAHatchScopedKey()
    {
        var guard = typeof(TrunkBuildController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- Harness ----

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private static TrunkBuildRequest Passed() => new(Remote, "main", Sha, BuildVerdicts.Passed, null, "box:/work/repo");

    private static TrunkBuildRequest Pending() => Passed() with { Verdict = BuildVerdicts.Pending };

    private static TrunkBuildRequest Failed(params string[] names) =>
        Passed() with
        {
            Verdict = BuildVerdicts.Failed,
            Failing = names.Select(n => new FailingCheckDto(n, $"https://forge.example/checks/{n}")).ToList(),
        };

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required TrunkBuildController Build { get; init; }
        public required IssuesController Issues { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }

        public Task<ActionResult<TrunkBuildDto>> PutAsync(TrunkBuildRequest request) => Build.PutTrunkBuild(request, default);

        /// <summary>Files a bug the way a person would, and answers with its key and its row id.</summary>
        public async Task<(string Key, long Id)> FileBugAsync()
        {
            var dto = Created(await Issues.CreateIssue(
                new IssueCreateRequest(ProjectId, "bug", "Build failing on main", null, null, null, null), default));

            IssueKey.TryParse(dto.Key, out var projectKey, out var number);
            var id = await Db.Issues.WithKey(projectKey, number).Select(i => i.Id).SingleAsync();
            return (dto.Key, id);
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
        var caller = new MergeCheckControllerTests.StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now } };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Build = new TrunkBuildController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time),
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

    private static string Reason(ActionResult<TrunkBuildDto> result) => ReasonOf(result.Result);

    private static string ReasonOf(IActionResult? result) => result switch
    {
        BadRequestObjectResult o => o.Value?.ToString() ?? "",
        var other => throw new InvalidOperationException($"expected a refusal, got {other?.GetType().Name ?? "a value"}"),
    };
}

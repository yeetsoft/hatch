using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a check session may record about an issue - see
/// <see cref="EfHatchVerdict"/> for the row and why a check playbook has to
/// cover the issue's live claim before one is kept.
/// </summary>
public class VerdictsControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Token = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ---- Kept ----

    [Theory]
    [InlineData(CheckVerdicts.Pass)]
    [InlineData(CheckVerdicts.Fail)]
    public async Task PassOrFail_IsKept_ByAPersonCaller(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var playbook = await h.AddPlaybookAsync(isCheck: true);
        await h.ClaimAsync(issue, playbook, Token);

        var result = await h.PostAsync(issue, new VerdictCreateRequest(verdict, "looks fine"));

        Assert.IsType<NoContentResult>(result);
        var kept = await h.Db.Verdicts.SingleAsync();
        Assert.Equal(verdict, kept.Verdict);
        Assert.Equal("looks fine", kept.Findings);
        Assert.Equal("Nathan", kept.Author);
        Assert.Equal(Token, kept.ClaimToken);
        Assert.Equal(playbook, kept.PlaybookId);
        Assert.Equal(Now, kept.CreatedAt);
        Assert.Null(kept.AppliedAt);
    }

    [Fact]
    public async Task PassOrFail_IsKept_ByAHatchScopedKeyCaller()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var playbook = await h.AddPlaybookAsync(isCheck: true);
        await h.ClaimAsync(issue, playbook, Token);

        h.Caller.Person = null;
        h.Caller.Key = new EfApiKey { Name = "a runner", Hash = [1], Prefix = "hatch_ak_x", CreatedAt = Now };

        var result = await h.PostAsync(issue, new VerdictCreateRequest(CheckVerdicts.Pass, null));

        Assert.IsType<NoContentResult>(result);
        var kept = await h.Db.Verdicts.SingleAsync();
        Assert.Equal("a runner", kept.Author);
        Assert.Null(kept.Findings);
    }

    [Fact]
    public async Task ASecondRecordUnderTheSameClaimToken_ReplacesTheFirst()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var playbook = await h.AddPlaybookAsync(isCheck: true);
        await h.ClaimAsync(issue, playbook, Token);

        Assert.IsType<NoContentResult>(await h.PostAsync(issue, new VerdictCreateRequest(CheckVerdicts.Fail, "first pass")));
        h.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.IsType<NoContentResult>(await h.PostAsync(issue, new VerdictCreateRequest(CheckVerdicts.Pass, "second pass")));

        var rows = await h.Db.Verdicts.ToListAsync();
        var kept = Assert.Single(rows);
        Assert.Equal(CheckVerdicts.Pass, kept.Verdict);
        Assert.Equal("second pass", kept.Findings);
        Assert.Equal(Now + TimeSpan.FromMinutes(5), kept.CreatedAt);
    }

    // ---- Every refusal ----

    [Fact]
    public async Task NoLiveClaim_IsRefusedAndWritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();

        var reason = Reason(await h.PostAsync(issue, new VerdictCreateRequest(CheckVerdicts.Pass, null)));

        Assert.Equal($"a verdict needs a check playbook covering {issue.Key} where it stands, and none does", reason);
        Assert.Empty(await h.Db.Verdicts.ToListAsync());
    }

    [Fact]
    public async Task AClaimUnderAPlaybookThatIsNotACheck_IsRefusedAndWritesNothing()
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var playbook = await h.AddPlaybookAsync(isCheck: false);
        await h.ClaimAsync(issue, playbook, Token);

        var reason = Reason(await h.PostAsync(issue, new VerdictCreateRequest(CheckVerdicts.Pass, null)));

        Assert.Equal($"a verdict needs a check playbook covering {issue.Key} where it stands, and none does", reason);
        Assert.Empty(await h.Db.Verdicts.ToListAsync());
    }

    [Theory]
    [InlineData("merged")]
    [InlineData("")]
    [InlineData("PASS")]
    public async Task AVerdictThatIsNotPassOrFail_IsRefusedAndWritesNothing(string verdict)
    {
        var h = await NewAsync();
        var issue = await h.FileAsync();
        var playbook = await h.AddPlaybookAsync(isCheck: true);
        await h.ClaimAsync(issue, playbook, Token);

        var reason = Reason(await h.PostAsync(issue, new VerdictCreateRequest(verdict, null)));

        Assert.Equal($"\"{verdict}\" is not a verdict - it is \"pass\" or \"fail\"", reason);
        Assert.Empty(await h.Db.Verdicts.ToListAsync());
    }

    // ---- The gate ----

    [Fact]
    public void WritingOne_IsOpenToAHatchScopedKey()
    {
        var guard = typeof(VerdictsController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        // The caller is whatever ran the check, and a verdict only a person
        // could enter would be one an agent session could never record for
        // itself.
        Assert.True(guard.AcceptKey);
    }

    // ---- Harness ----

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required VerdictsController Verdicts { get; init; }
        public required IssuesController Issues { get; init; }
        public required StubCallerIdentity Caller { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required int ProjectId { get; init; }
        public required int FromStatusId { get; init; }
        public required int ToStatusId { get; init; }

        public async Task<IssueDto> FileAsync() =>
            Created(await Issues.CreateIssue(new IssueCreateRequest(ProjectId, "task", "a thing", null, null, null, null), default));

        public async Task<int> AddPlaybookAsync(bool isCheck)
        {
            var playbook = new EfHatchPlaybook
            {
                FromStatusId = FromStatusId,
                ToStatusId = ToStatusId,
                Types = isCheck ? "check" : "not-check",
                Shape = EfHatchPlaybook.AnyShape,
                Prompt = "look at it",
                Model = "sonnet",
                Effort = "medium",
                IsCheck = isCheck,
                CreatedAt = Now,
                UpdatedAt = Now,
            };
            Db.Playbooks.Add(playbook);
            await Db.SaveChangesAsync();
            return playbook.Id;
        }

        /// <summary>Sets the issue's claim columns directly, the way a take would have left them, without going through <c>TakeClaim</c>.</summary>
        public async Task ClaimAsync(IssueDto issue, int playbookId, Guid token)
        {
            IssueKey.TryParse(issue.Key, out var projectKey, out var number);
            var row = await Db.Issues.WithKey(projectKey, number).SingleAsync();
            row.ClaimPlaybookId = playbookId;
            row.ClaimToken = token;
            await Db.SaveChangesAsync();
        }

        public Task<IActionResult> PostAsync(IssueDto issue, VerdictCreateRequest request) =>
            Verdicts.PostVerdict(issue.Key, request, default);
    }

    private static async Task<Harness> NewAsync()
    {
        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var hatch = new EfHatchProject { Key = "AER", Name = "Hatch", CreatedAt = Now };
        db.Add(hatch);
        var inbox = new EfHatchStatus { Name = "inbox", SortOrder = 10 };
        var doing = new EfHatchStatus { Name = "doing", SortOrder = 20 };
        db.Add(inbox);
        db.Add(doing);
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(Now);
        var caller = new StubCallerIdentity
        {
            Person = new EfPerson { Name = "Nathan", CreatedAt = Now, UpdatedAt = Now },
        };
        var actors = new StubActorDirectory();

        return new Harness
        {
            Db = db,
            Verdicts = new VerdictsController(db, caller, time),
            Issues = new IssuesController(db, new RankService(db), actors, TestClaims.With(), caller, time, new ProjectAccess(db, actors)),
            Caller = caller,
            Time = time,
            ProjectId = hatch.Id,
            FromStatusId = inbox.Id,
            ToStatusId = doing.Id,
        };
    }

    /// <summary>Whoever the test says is holding the phone. Their name is the audit actor.</summary>
    public sealed class StubCallerIdentity : ICallerIdentity
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

    private static T Created<T>(ActionResult<T> result) =>
        result.Result is CreatedAtActionResult created
            ? (T)created.Value!
            : result.Value ?? throw new InvalidOperationException($"expected a created value, got {result.Result?.GetType().Name ?? "nothing"}");

    /// <summary>The plain-text reason on a refusal, and a failure if it was not one.</summary>
    private static string Reason(IActionResult result) => result switch
    {
        BadRequestObjectResult o => o.Value?.ToString() ?? "",
        var other => throw new InvalidOperationException($"expected a refusal, got {other.GetType().Name}"),
    };
}

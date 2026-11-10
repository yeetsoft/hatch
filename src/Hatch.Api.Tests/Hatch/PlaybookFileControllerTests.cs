using System.Security.Cryptography;
using System.Text;
using Hatch.Api.Common;
using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Hatch.Api.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="PlaybookFileController"/>: the route's own validation, the
/// last-good-sha/hash bookkeeping across a sequence of writes, and reading
/// the stored rows back - modelled on <see cref="TrunkBuildControllerTests"/>.
/// </summary>
public class PlaybookFileControllerTests
{
    private const string Remote = "git@forge.example:owner/repo.git";
    private const string Trunk = "main";
    private const string Url = "https://forge.example/blob/.hatch/playbooks.yaml";
    private const string Runner = "box:/work/repo";

    private const string Sha1 = "1111111111111111111111111111111111111111";
    private const string Sha2 = "2222222222222222222222222222222222222222";
    private const string Sha3 = "3333333333333333333333333333333333333333";

    private const string GoodYaml = """
        playbooks:
          - from: Breakdown
            to: Backlog
            types: [epic]
            shape: any
            model: fable
            effort: max
            budget: 5
            briefLimit: 6000
            quietLimitSeconds: 1500
            maxRunSeconds: 3600
            prompt: |
              You are turning a captured idea into an epic...
        """;

    private static readonly string Good = Content(GoodYaml);
    private static readonly string Bad = Content("foo: bar");

    private static readonly string GoodHash = Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(Good)));

    // ---- The gate ----

    [Fact]
    public void WritingOne_IsOpenToAKey()
    {
        var guard = typeof(PlaybookFileController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.True(guard.AcceptKey);
    }

    // ---- Every refusal ----

    [Fact]
    public async Task ABadRemote_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal("a remote can't be empty", Reason(await h.PutAsync(Request(Good, Sha1) with { Remote = "  " })));
        Assert.Contains("remote", Reason(await h.PutAsync(Request(Good, Sha1) with { Remote = "nonsense" })));
        Assert.Empty(await h.Db.PlaybookFiles.ToListAsync());
    }

    [Fact]
    public async Task AnEmptyOrOverlongTrunk_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a playbook file names the trunk it is about",
            Reason(await h.PutAsync(Request(Good, Sha1) with { Trunk = "" })));
        Assert.Contains(
            "at most",
            Reason(await h.PutAsync(Request(Good, Sha1) with { Trunk = new string('t', EfHatchPlaybookFile.MaxRefLength + 1) })));
    }

    [Fact]
    public async Task AnEmptyOrOverlongSha_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a playbook file names the sha it is about",
            Reason(await h.PutAsync(Request(Good, ""))));
        Assert.Contains(
            "at most",
            Reason(await h.PutAsync(Request(Good, new string('a', EfHatchPlaybookFile.MaxShaLength + 1)))));
    }

    [Fact]
    public async Task AnEmptyOrOverlongRunner_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a playbook file names the runner that took it",
            Reason(await h.PutAsync(Request(Good, Sha1) with { Runner = " " })));
        Assert.Contains(
            "at most",
            Reason(await h.PutAsync(Request(Good, Sha1) with { Runner = new string('r', ClaimRequest.MaxRunnerLength + 1) })));
    }

    [Fact]
    public async Task NonBase64Content_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal(
            "content is not valid base64",
            Reason(await h.PutAsync(Request(Good, Sha1) with { Content = "not-valid-base64!!" })));
    }

    [Fact]
    public async Task ContentOverTheSizeLimit_IsRefused()
    {
        var h = await NewAsync();

        var overlong = Convert.ToBase64String(new byte[512 * 1024 + 1]);
        Assert.Equal(
            "a playbook file is at most 512 KB",
            Reason(await h.PutAsync(Request(overlong, Sha1))));
    }

    // ---- The last-good sha/hash, across a sequence of writes ----

    [Fact]
    public async Task ARepeatAtTheSameShaAndHash_ChangesOnlyTheStamps()
    {
        var h = await NewAsync();

        var first = Value(await h.PutAsync(Request(Good, Sha1)));

        h.Time.Advance(TimeSpan.FromMinutes(5));
        var second = Value(await h.PutAsync(Request(Good, Sha1)));

        Assert.Equal(first.State, second.State);
        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(first.Rows.Select(r => r.Prompt), second.Rows.Select(r => r.Prompt));
        Assert.Equal(first.Problem, second.Problem);
        Assert.Equal(h.Now.AddMinutes(5), second.CheckedAt);
    }

    [Fact]
    public async Task ANewShaThatIsInvalid_KeepsTheLastGoodShaAndHash()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1));
        var kept = Value(await h.PutAsync(Request(Bad, Sha2)));

        Assert.Equal(Sha1, kept.LastGoodSha);
        Assert.Equal(GoodHash, kept.LastGoodHash);
    }

    [Fact]
    public async Task ANewShaThatIsAbsent_LeavesTheLastGoodShaAndHashUntouched()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1));
        await h.PutAsync(Request(Bad, Sha2));

        var kept = Value(await h.PutAsync(Request(null, Sha3)));

        Assert.Equal(Sha1, kept.LastGoodSha);
        Assert.Equal(GoodHash, kept.LastGoodHash);
    }

    [Fact]
    public async Task ANewShaThatParses_ClearsTheLastGoodShaAndHash()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1));
        await h.PutAsync(Request(Bad, Sha2));

        var kept = Value(await h.PutAsync(Request(Good, Sha3)));

        Assert.Null(kept.LastGoodSha);
        Assert.Null(kept.LastGoodHash);
    }

    [Fact]
    public async Task ABadWriteAfterAnotherBadWrite_DoesNotOverwriteTheOriginalLastGood()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1));
        await h.PutAsync(Request(Bad, Sha2));
        var kept = Value(await h.PutAsync(Request(Bad, Sha3)));

        Assert.Equal(Sha1, kept.LastGoodSha);
        Assert.Equal(GoodHash, kept.LastGoodHash);
    }

    [Fact]
    public async Task HashIsTheSha256OfTheDecodedBytes()
    {
        var h = await NewAsync();

        var kept = Value(await h.PutAsync(Request(Good, Sha1)));

        Assert.Equal(GoodHash, kept.Hash);
    }

    // ---- One row per repository per trunk ----

    [Fact]
    public async Task TheSameRepositorySpelledTwoWays_IsOneRow()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1) with { Remote = "git@forge.example:owner/repo.git" });
        await h.PutAsync(Request(Good, Sha2) with { Remote = "https://forge.example/owner/repo" });

        Assert.Single(await h.Db.PlaybookFiles.ToListAsync());
    }

    // ---- Reading ----

    [Fact]
    public async Task GetPlaybookFiles_AnswersPathAndFiles_OrderedByCanonicalThenTrunk()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1) with { Remote = "git@forge.example:owner/repo.git", Trunk = "release" });
        await h.PutAsync(Request(Good, Sha1) with { Remote = "git@forge.example:owner/repo.git", Trunk = "main" });
        await h.PutAsync(Request(Good, Sha1) with { Remote = "git@forge.example:owner/another.git", Trunk = "main" });

        var answer = Value(await h.GetAsync());

        Assert.Equal(PlaybookFile.Path, answer.Path);
        Assert.Equal(
            [("forge.example/owner/another", "main"), ("forge.example/owner/repo", "main"), ("forge.example/owner/repo", "release")],
            answer.Files.Select(f => (f.Canonical, f.Trunk)));
    }

    [Fact]
    public async Task AParsedRow_ResolvesCurrentStatusNames()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1));

        var breakdown = await h.Db.Statuses.SingleAsync(s => s.Name == "Breakdown");
        breakdown.Name = "Shaping";
        await h.Db.SaveChangesAsync();

        var answer = Value(await h.GetAsync());

        var row = Assert.Single(Assert.Single(answer.Files).Rows);
        Assert.Equal("Shaping", row.FromStatusName);
    }

    [Fact]
    public async Task AParsedRow_ReadsBackItsQuietLimitSeconds()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1));

        var row = Assert.Single(Assert.Single(Value(await h.GetAsync()).Files).Rows);
        Assert.Equal(1500, row.QuietLimitSeconds);
    }

    [Fact]
    public async Task AParsedRow_ReadsBackItsMaxRunSeconds()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1));

        var row = Assert.Single(Assert.Single(Value(await h.GetAsync()).Files).Rows);
        Assert.Equal(3600, row.MaxRunSeconds);
    }

    // ---- Harness ----

    private static string Content(string yaml) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(yaml.EndsWith('\n') ? yaml : yaml + "\n"));

    private static PlaybookFileRequest Request(string? content, string sha) =>
        new(Remote, Trunk, sha, content, Url, Runner);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required PlaybookFileController Files { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required DateTimeOffset Now { get; init; }

        public Task<ActionResult<PlaybookFileDto>> PutAsync(PlaybookFileRequest request) => Files.PutPlaybookFile(request, default);

        public Task<ActionResult<PlaybookFilesDto>> GetAsync() => Files.GetPlaybookFiles(default);

        /// <summary>Adds the columns past Backlog the review and merge rows need: Review, a protected Approved that merges, and a terminal Done.</summary>
        public async Task FullBoardAsync()
        {
            Db.Add(new EfHatchStatus { Name = "Review", SortOrder = 30 });
            Db.Add(new EfHatchStatus { Name = "Approved", SortOrder = 40, IsProtected = true, MergesPullRequest = true });
            Db.Add(new EfHatchStatus { Name = "Done", SortOrder = 50, IsTerminal = true });
            await Db.SaveChangesAsync();
        }

        public async Task MoveColumnAsync(string name, int sortOrder)
        {
            (await Db.Statuses.SingleAsync(s => s.Name == name)).SortOrder = sortOrder;
            await Db.SaveChangesAsync();
        }
    }

    private static async Task<Harness> NewAsync()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        db.Add(new EfHatchStatus { Name = "Breakdown", SortOrder = 10 });
        db.Add(new EfHatchStatus { Name = "Backlog", SortOrder = 20 });
        await db.SaveChangesAsync();

        var time = new FakeTimeProvider(now);
        var caller = new MergeCheckControllerTests.StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = now, UpdatedAt = now } };

        return new Harness
        {
            Db = db,
            Files = new PlaybookFileController(db, caller, time),
            Time = time,
            Now = now,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {ReasonOf(result.Result)}");

    // ---- UnreachableReason: read from the board as it stands, never stored ----

    private const string Never = " - this row can never run";

    private static string RowYaml(string from, string to, bool isCheck = false) => $"""
          - from: {from}
            to: {to}
            types: [epic]
            shape: any
            model: fable
            effort: max
            isCheck: {(isCheck ? "true" : "false")}
            prompt: |
              A prompt.
        """;

    private static string FileOf(params string[] rows) => "playbooks:\n" + string.Join("\n", rows);

    [Fact]
    public async Task ARowAimedPastTheNextColumn_ReadsBackWithTheSentenceNamingTheRealTarget()
    {
        var h = await NewAsync();
        await h.FullBoardAsync();

        await h.PutAsync(Request(Content(FileOf(RowYaml("Breakdown", "Review"))), Sha1));
        var row = Assert.Single(Assert.Single(Value(await h.GetAsync()).Files).Rows);

        Assert.Equal("a dispatch from \"Breakdown\" goes to \"Backlog\", not \"Review\"" + Never, row.UnreachableReason);
    }

    [Fact]
    public async Task ARowAtTheNextColumn_TheReviewRowToItself_AndACheckIntoTheMergeColumn_ReadBackWithNoSentence()
    {
        var h = await NewAsync();
        await h.FullBoardAsync();

        var file = FileOf(
            RowYaml("Breakdown", "Backlog"), RowYaml("Review", "Review"), RowYaml("Review", "Approved", isCheck: true));
        Assert.Equal("parsed", Value(await h.PutAsync(Request(Content(file), Sha1))).State);

        var rows = Assert.Single(Value(await h.GetAsync()).Files).Rows;

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Null(r.UnreachableReason));
    }

    [Fact]
    public async Task ReorderingColumns_FlipsTheReadingOnTheNextGet_WithNoNewPut()
    {
        var h = await NewAsync();
        await h.FullBoardAsync();
        await h.PutAsync(Request(Content(FileOf(RowYaml("Breakdown", "Backlog"))), Sha1));
        var before = Assert.Single(Value(await h.GetAsync()).Files);
        Assert.Null(Assert.Single(before.Rows).UnreachableReason);

        await h.MoveColumnAsync("Review", 15);
        var flipped = Assert.Single(Value(await h.GetAsync()).Files);

        Assert.Equal(
            "a dispatch from \"Breakdown\" goes to \"Review\", not \"Backlog\"" + Never,
            Assert.Single(flipped.Rows).UnreachableReason);
        Assert.Equal(before.CheckedAt, flipped.CheckedAt);
        Assert.Equal(before.Sha, flipped.Sha);

        await h.MoveColumnAsync("Review", 30);
        Assert.Null(Assert.Single(Assert.Single(Value(await h.GetAsync()).Files).Rows).UnreachableReason);
    }

    [Fact]
    public async Task AFileCarryingAnUnreachableRow_IsStillStoredParsed_AndThePutResponseCarriesTheSentence()
    {
        var h = await NewAsync();
        await h.FullBoardAsync();

        var put = Value(await h.PutAsync(Request(Content(FileOf(RowYaml("Breakdown", "Review"))), Sha1)));

        Assert.Equal("parsed", put.State);
        Assert.Equal(
            "a dispatch from \"Breakdown\" goes to \"Backlog\", not \"Review\"" + Never,
            Assert.Single(put.Rows).UnreachableReason);

        var stored = await h.Db.PlaybookFiles.SingleAsync();
        Assert.Equal("parsed", stored.State);
        Assert.NotNull(stored.Rows);
    }

    private static string Reason(ActionResult<PlaybookFileDto> result) => ReasonOf(result.Result);

    private static string ReasonOf(IActionResult? result) => result switch
    {
        BadRequestObjectResult o => o.Value?.ToString() ?? "",
        var other => throw new InvalidOperationException($"expected a refusal, got {other?.GetType().Name ?? "a value"}"),
    };
}

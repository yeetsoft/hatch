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
/// <see cref="DependencyFileController"/>: the route's own validation, the
/// last-good-sha/hash bookkeeping across a sequence of writes, and reading
/// the stored rows back - modelled on <see cref="PlaybookFileControllerTests"/>.
/// </summary>
public class DependencyFileControllerTests
{
    private const string Remote = "git@forge.example:owner/repo.git";
    private const string Trunk = "main";
    private const string Url = "https://forge.example/blob/.hatch/dependencies.json";
    private const string Runner = "box:/work/repo";

    private const string Sha1 = "1111111111111111111111111111111111111111";
    private const string Sha2 = "2222222222222222222222222222222222222222";
    private const string Sha3 = "3333333333333333333333333333333333333333";

    private const string GoodJson = """{"dependencies":["make"]}""";
    private const string BadJson = """{"foo":"bar"}""";

    private static readonly string Good = Content(GoodJson);
    private static readonly string Bad = Content(BadJson);

    private static readonly string GoodHash = Convert.ToHexStringLower(SHA256.HashData(Convert.FromBase64String(Good)));

    // ---- The gate ----

    [Fact]
    public void WritingOne_IsOpenToAHatchScopedKey()
    {
        var guard = typeof(DependencyFileController)
            .GetCustomAttributes(typeof(RequireRoleAttribute), inherit: false)
            .Cast<RequireRoleAttribute>()
            .Single();

        Assert.Equal(ApiKeyScopes.Hatch, guard.AcceptScope);
    }

    // ---- Every refusal ----

    [Fact]
    public async Task ABadRemote_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal("a remote can't be empty", Reason(await h.PutAsync(Request(Good, Sha1) with { Remote = "  " })));
        Assert.Contains("remote", Reason(await h.PutAsync(Request(Good, Sha1) with { Remote = "nonsense" })));
        Assert.Empty(await h.Db.DependencyFiles.ToListAsync());
    }

    [Fact]
    public async Task AnEmptyOrOverlongTrunk_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a dependency file names the trunk it is about",
            Reason(await h.PutAsync(Request(Good, Sha1) with { Trunk = "" })));
        Assert.Contains(
            "at most",
            Reason(await h.PutAsync(Request(Good, Sha1) with { Trunk = new string('t', EfHatchDependencyFile.MaxRefLength + 1) })));
    }

    [Fact]
    public async Task AnEmptyOrOverlongSha_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a dependency file names the sha it is about",
            Reason(await h.PutAsync(Request(Good, ""))));
        Assert.Contains(
            "at most",
            Reason(await h.PutAsync(Request(Good, new string('a', EfHatchDependencyFile.MaxShaLength + 1)))));
    }

    [Fact]
    public async Task AnEmptyOrOverlongRunner_IsRefused()
    {
        var h = await NewAsync();

        Assert.Equal(
            "a dependency file names the runner that took it",
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
            "a dependency file is at most 512 KB",
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
        Assert.Equal(first.Entries.Select(e => e.Id), second.Entries.Select(e => e.Id));
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

    // ---- An unknown dependency id is a parse-time Problem, not a refusal ----

    [Fact]
    public async Task AnUnknownDependencyId_ParsesToInvalid()
    {
        var h = await NewAsync();

        var kept = Value(await h.PutAsync(Request(Content("""{"dependencies":["not-a-real-dependency"]}"""), Sha1)));

        Assert.Equal("invalid", kept.State);
        Assert.NotNull(kept.Hash);
        Assert.Contains("not-a-real-dependency", kept.Problem);
    }

    // ---- One row per repository per trunk ----

    [Fact]
    public async Task TheSameRepositorySpelledTwoWays_IsOneRow()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1) with { Remote = "git@forge.example:owner/repo.git" });
        await h.PutAsync(Request(Good, Sha2) with { Remote = "https://forge.example/owner/repo" });

        Assert.Single(await h.Db.DependencyFiles.ToListAsync());
    }

    // ---- Reading ----

    [Fact]
    public async Task GetDependencyFiles_AnswersPathAndFiles_OrderedByCanonicalThenTrunk()
    {
        var h = await NewAsync();

        await h.PutAsync(Request(Good, Sha1) with { Remote = "git@forge.example:owner/repo.git", Trunk = "release" });
        await h.PutAsync(Request(Good, Sha1) with { Remote = "git@forge.example:owner/repo.git", Trunk = "main" });
        await h.PutAsync(Request(Good, Sha1) with { Remote = "git@forge.example:owner/another.git", Trunk = "main" });

        var answer = Value(await h.GetAsync());

        Assert.Equal(DependencyDeclaration.Path, answer.Path);
        Assert.Equal(
            [("forge.example/owner/another", "main"), ("forge.example/owner/repo", "main"), ("forge.example/owner/repo", "release")],
            answer.Files.Select(f => (f.Canonical, f.Trunk)));
    }

    [Fact]
    public async Task AParsedRow_ResolvesCurrentLabels()
    {
        var known = new Dependencies([new Dependency("make", "Make, renamed")]);
        var h = await NewAsync(known);

        await h.PutAsync(Request(Good, Sha1));

        var entry = Assert.Single(Assert.Single(Value(await h.GetAsync()).Files).Entries);
        Assert.Equal("make", entry.Id);
        Assert.Equal("Make, renamed", entry.Label);
    }

    // ---- Harness ----

    private static string Content(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

    private static DependencyFileRequest Request(string? content, string sha) =>
        new(Remote, Trunk, sha, content, Url, Runner);

    private sealed class Harness
    {
        public required HatchContext Db { get; init; }
        public required DependencyFileController Files { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required DateTimeOffset Now { get; init; }

        public Task<ActionResult<DependencyFileDto>> PutAsync(DependencyFileRequest request) => Files.PutDependencyFile(request, default);

        public Task<ActionResult<DependencyFilesDto>> GetAsync() => Files.GetDependencyFiles(default);
    }

    private static async Task<Harness> NewAsync(Dependencies? known = null)
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

        var db = new HatchContext(
            new DbContextOptionsBuilder<HatchContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var time = new FakeTimeProvider(now);
        var caller = new MergeCheckControllerTests.StubCallerIdentity { Person = new EfPerson { Name = "Nathan", CreatedAt = now, UpdatedAt = now } };

        return new Harness
        {
            Db = db,
            Files = new DependencyFileController(db, caller, time, known),
            Time = time,
            Now = now,
        };
    }

    private static T Value<T>(ActionResult<T> result) =>
        result.Value ?? throw new InvalidOperationException($"expected a value, got {ReasonOf(result.Result)}");

    private static string Reason(ActionResult<DependencyFileDto> result) => ReasonOf(result.Result);

    private static string ReasonOf(IActionResult? result) => result switch
    {
        BadRequestObjectResult o => o.Value?.ToString() ?? "",
        var other => throw new InvalidOperationException($"expected a refusal, got {other?.GetType().Name ?? "a value"}"),
    };
}

using Hatch.Api.Ef;
using Hatch.Api.Modules.Hatch;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What the go-to lookup finds, in what order, and for whom - against the Postgres
/// that ships, since the point of the route is the SQL it becomes.
///
/// <para>Skipped without <c>HATCH_TEST_DATABASE_URL</c>; <c>make test-api-db</c>
/// sets it. Three projects: <c>HA</c>, <c>HA3</c> (a key that holds a digit, so
/// <c>ha34</c> is two readings) and <c>ZZ</c>.</para>
/// </summary>
[Collection(HatchDatabaseCollection.Name)]
public class GoToControllerDatabaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    // ---- Numbers ----

    [SkippableFact]
    public async Task ANumber_IsAPrefixInEveryProject_ByNumberThenProjectKey_AndNeverAMiddle()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 4, 14, 40, 49, 400, 499, 5);
        await h.FileAsync(h.Zz, 4, 41);

        var result = await h.GoToAsync("4");

        Assert.Equal(
            ["HA-4", "ZZ-4", "HA-40", "ZZ-41", "HA-49", "HA-400", "HA-499"],
            result.Rows.Select(r => r.Key));
        Assert.False(result.More);
    }

    [SkippableFact]
    public async Task ANumber_AcrossManyDigitLengths_StaysInNumberOrder()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 45, 450, 4_500, 45_000, 450_000, 4_500_000, 99, 145, 1_450);

        var result = await h.GoToAsync("45");

        Assert.Equal(
            [45, 450, 4_500, 45_000, 450_000, 4_500_000],
            result.Rows.Select(r => r.Number));
    }

    [SkippableFact]
    public async Task ANumberWithALeadingZero_OrElevenDigits_FindsNothing()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 45, 450);

        Assert.Empty((await h.GoToAsync("045")).Rows);
        Assert.Empty((await h.GoToAsync("12345678901")).Rows);
    }

    [SkippableFact]
    public async Task ANumberPastTheTopOfAnInt_FindsNothing_AndDoesNotThrow()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 1, int.MaxValue);

        Assert.Empty((await h.GoToAsync("3000000000")).Rows);
        Assert.Equal(["HA-2147483647"], (await h.GoToAsync("21474")).Rows.Select(r => r.Key));
    }

    [SkippableFact]
    public async Task AClosedIssue_IsFound_WithItsColumn()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 7);
        await h.FileAsync(h.Ha, closed: true, 70);

        var result = await h.GoToAsync("7");

        var open = result.Rows[0];
        Assert.Equal(("HA-7", "HA", 7, "story", "issue 7"), (open.Key, open.ProjectKey, open.Number, open.Type, open.Title));
        Assert.Equal(("todo", "#3366cc", h.TodoId), (open.StatusName, open.StatusColor, open.StatusId));

        var closed = result.Rows[1];
        Assert.Equal("HA-70", closed.Key);
        Assert.Equal(("done", "#22aa44", h.DoneId), (closed.StatusName, closed.StatusColor, closed.StatusId));
        Assert.All(result.Rows, r => Assert.False(r.OutOfScope));
    }

    [SkippableFact]
    public async Task ANumberRead_IsOneBranchPerDigitLength_JoinedWithUnionAll()
    {
        await using var h = await NewAsync();
        await using var db = h.Connect();

        // 123 is 3 digits to 10: eight reads, seven joins.
        var sql = GoToController.NumberQueryString(db.Issues, "123", 51);

        Assert.Equal(7, sql.Split("UNION ALL").Length - 1);
    }

    // ---- Keys ----

    [SkippableFact]
    public async Task AKeyPrefix_AgreesWhateverThePunctuationAndCase_AndNeverMatchesAMiddle()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 4, 14, 40, 5);

        foreach (var typed in new[] { "ha-4", "ha4", "HA-4", "Ha4", "  ha-4 " })
            Assert.Equal(["HA-4", "HA-40"], (await h.GoToAsync(typed)).Rows.Select(r => r.Key));
    }

    [SkippableFact]
    public async Task AProjectPrefix_ListsEveryIssueOfEveryProjectItStartsTheKeyOf_ByNumberThenKey()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 2, 1);
        await h.FileAsync(h.Ha3, 1, 2);
        await h.FileAsync(h.Zz, 1);

        var result = await h.GoToAsync("ha");

        Assert.Equal(["HA-1", "HA3-1", "HA-2", "HA3-2"], result.Rows.Select(r => r.Key));
    }

    [SkippableFact]
    public async Task AKeyWithADigitInIt_ReadsBothWays_ByNumberThenKey()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 34, 340, 3, 35);
        await h.FileAsync(h.Ha3, 4, 40, 5);

        // HA + 34, and HA3 + 4. HA-340 is a prefix of 34 and HA3-40 of 4.
        var result = await h.GoToAsync("ha34");

        Assert.Equal(["HA3-4", "HA-34", "HA3-40", "HA-340"], result.Rows.Select(r => r.Key));
    }

    [SkippableFact]
    public async Task AKeyPrefixThatIsAlsoAProjectKey_DoesNotListAnIssueTwice()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha3, 4);

        // "ha3" starts the key HA3, and also splits as HA + 3.
        var result = await h.GoToAsync("ha3");

        Assert.Equal(["HA3-4"], result.Rows.Select(r => r.Key));
    }

    // ---- A whole key, outside the scope ----

    [SkippableFact]
    public async Task AWholeKey_OutsideTheProjectsGiven_ComesFirst_MarkedOutOfScope()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 4, 40);
        await h.FileAsync(h.Zz, 4, 40);

        var result = await h.GoToAsync("zz-4", h.Ha);

        // ZZ-40 is a prefix match of a project that is not given, so it is not offered.
        Assert.Equal(["ZZ-4"], result.Rows.Select(r => r.Key));
        Assert.True(Assert.Single(result.Rows).OutOfScope);
    }

    [SkippableFact]
    public async Task AWholeKey_OutsideTheProjectsGiven_NeverListsItsPrefixMatches()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 4, 40);
        await h.FileAsync(h.Ha3, 4, 40);

        // ha-4 typed with HA3 given: HA-4 is the key, and is outside; HA-40 only starts it.
        var result = await h.GoToAsync("ha-4", h.Ha3);

        Assert.Equal(["HA-4"], result.Rows.Select(r => r.Key));
        Assert.True(Assert.Single(result.Rows).OutOfScope);
    }

    [SkippableFact]
    public async Task AWholeKey_InsideTheScope_IsNotFlagged()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 4);

        var result = await h.GoToAsync("ha-4", h.Ha);

        Assert.False(Assert.Single(result.Rows).OutOfScope);
    }

    [SkippableFact]
    public async Task APrefixOutsideTheProjectsGiven_IsNotFound_NorANumber()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Zz, 4, 40);

        Assert.Empty((await h.GoToAsync("zz", h.Ha)).Rows);
        Assert.Empty((await h.GoToAsync("4", h.Ha)).Rows);
    }

    [SkippableFact]
    public async Task WithNoProjectsGiven_NoWholeKeyIsOutOfScope()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Zz, 4);

        var result = await h.GoToAsync("zz-4");

        Assert.False(Assert.Single(result.Rows).OutOfScope);
    }

    [SkippableFact]
    public async Task TwoReadingsOfAWholeKey_BothOutsideTheScope_ComeFirstInOrder()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 34);
        await h.FileAsync(h.Ha3, 4);

        var result = await h.GoToAsync("ha34", h.Zz);

        Assert.Equal(["HA3-4", "HA-34"], result.Rows.Select(r => r.Key));
        Assert.All(result.Rows, r => Assert.True(r.OutOfScope));
    }

    // ---- Nothing, and the limit ----

    [SkippableFact]
    public async Task TitleWords_Blank_AndAKeyThenANumber_AnswerNoRows()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 4);

        foreach (var typed in new[] { "gasket", "issue 4", "", "   ", "ha 4", null })
        {
            var result = await h.GoToAsync(typed);
            Assert.Empty(result.Rows);
            Assert.False(result.More);
        }
    }

    [SkippableFact]
    public async Task MoreThanTheLimit_IsCutToIt_AndSaysMore()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 1, 2, 3, 4, 5);

        var result = await h.GoToAsync("ha", limit: 2);

        Assert.Equal(["HA-1", "HA-2"], result.Rows.Select(r => r.Key));
        Assert.True(result.More);
    }

    [SkippableFact]
    public async Task ExactlyTheLimit_SaysNoMore()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 1, 2);

        var result = await h.GoToAsync("ha", limit: 2);

        Assert.Equal(2, result.Rows.Count);
        Assert.False(result.More);
    }

    [SkippableFact]
    public async Task TheLimit_CutsANumberRead_AndASplitRead()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 1, 10, 11, 12, 100);
        await h.FileAsync(h.Ha3, 1);

        var byNumber = await h.GoToAsync("1", limit: 3);
        Assert.Equal(["HA-1", "HA3-1", "HA-10"], byNumber.Rows.Select(r => r.Key));
        Assert.True(byNumber.More);

        var bySplit = await h.GoToAsync("ha1", limit: 2);
        Assert.Equal(["HA-1", "HA-10"], bySplit.Rows.Select(r => r.Key));
        Assert.True(bySplit.More);
    }

    [SkippableFact]
    public async Task AnOutOfScopeRow_CountsAgainstTheLimit()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 34, 340);
        await h.FileAsync(h.Ha3, 4);

        // ha34 with HA given: HA3-4 is outside; HA-34 and HA-340 are inside. Three rows.
        var cut = await h.GoToAsync("ha34", h.Ha, limit: 2);
        Assert.Equal(["HA3-4", "HA-34"], cut.Rows.Select(r => r.Key));
        Assert.True(cut.More);

        var whole = await h.GoToAsync("ha34", h.Ha, limit: 3);
        Assert.Equal(["HA3-4", "HA-34", "HA-340"], whole.Rows.Select(r => r.Key));
        Assert.False(whole.More);
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(51)]
    [InlineData(-1)]
    public async Task ALimitOutsideOneToFifty_IsRefused(int limit)
    {
        await using var h = await NewAsync();

        var answer = await new GoToController(h.Connect()).GetGoTo("1", null, limit, default);

        var refused = Assert.IsType<BadRequestObjectResult>(answer.Result);
        Assert.Equal($"a limit is between 1 and 50 - not {limit}", refused.Value);
    }

    // ---- Scope ----

    [SkippableFact]
    public async Task APersonScopedToOneProject_NeverGetsARowFromAnother_ByNumberPrefixOrWholeKey()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 4, 40);
        await h.FileAsync(h.Zz, 4, 40);

        foreach (var typed in new[] { "4", "ha", "ha-4", "ha4", "zz", "zz-4", "zz4" })
        {
            foreach (var narrowed in new int?[] { null, h.Ha, h.Zz })
            {
                var seen = await h.GoToAsync(typed, narrowed, scopedTo: h.Ha);
                Assert.DoesNotContain(seen.Rows, r => r.ProjectKey == "ZZ");
            }
        }

        Assert.Equal(["HA-4", "HA-40"], (await h.GoToAsync("4", scopedTo: h.Ha)).Rows.Select(r => r.Key));
        Assert.Equal(["HA-4", "HA-40"], (await h.GoToAsync("ha", scopedTo: h.Ha)).Rows.Select(r => r.Key));
    }

    [SkippableFact]
    public async Task AWholeKeyThePersonCannotSee_IsNoRow_NotAnOutOfScopeOne()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 4);
        await h.FileAsync(h.Zz, 4);

        // Narrowed to HA, and scoped to HA as well: ZZ-4 is not theirs to be told of.
        var result = await h.GoToAsync("zz-4", h.Ha, scopedTo: h.Ha);

        Assert.Empty(result.Rows);
    }

    [SkippableFact]
    public async Task ASplitReading_InAProjectThePersonCannotSee_IsNotListed()
    {
        await using var h = await NewAsync();
        await h.FileAsync(h.Ha, 34);
        await h.FileAsync(h.Ha3, 4);

        var result = await h.GoToAsync("ha34", scopedTo: h.Ha);

        Assert.Equal(["HA-34"], result.Rows.Select(r => r.Key));
    }

    // ---- Harness ----

    private static async Task<Harness> NewAsync()
    {
        Skip.IfNot(
            HatchDatabase.Available,
            "HATCH_TEST_DATABASE_URL is unset - the go-to read is a union of ranged reads and what Postgres " +
            "makes of it is the point. Run `make test-api-db`, or point the variable at a scratch database.");

        var connectionString = await HatchDatabase.PrepareAsync();
        var options = new DbContextOptionsBuilder<HatchContext>().UseNpgsql(connectionString).Options;

        await using var db = new HatchContext(options);

        var ha = new EfHatchProject { Key = "HA", Name = "Hatch", CreatedAt = Now };
        var ha3 = new EfHatchProject { Key = "HA3", Name = "Hatch three", CreatedAt = Now };
        var zz = new EfHatchProject { Key = "ZZ", Name = "Zed", CreatedAt = Now };
        var todo = new EfHatchStatus { Name = "todo", SortOrder = 20, Color = "#3366cc" };
        var done = new EfHatchStatus { Name = "done", SortOrder = 90, IsTerminal = true, Color = "#22aa44" };
        db.AddRange(ha, ha3, zz, todo, done);
        await db.SaveChangesAsync();

        return new Harness(options, ha.Id, ha3.Id, zz.Id, todo.Id, done.Id);
    }

    private sealed class Harness(
        DbContextOptions<HatchContext> options, int ha, int ha3, int zz, int todoId, int doneId) : IAsyncDisposable
    {
        private readonly List<HatchContext> open = [];

        public int Ha { get; } = ha;
        public int Ha3 { get; } = ha3;
        public int Zz { get; } = zz;
        public int TodoId { get; } = todoId;
        public int DoneId { get; } = doneId;

        public HatchContext Connect()
        {
            var db = new HatchContext(options);
            open.Add(db);
            return db;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var db in open) await db.DisposeAsync();
        }

        /// <summary>Open issues placed directly, titled "issue N".</summary>
        public Task FileAsync(int projectId, params int[] numbers) => FileAsync(projectId, closed: false, numbers);

        public async Task FileAsync(int projectId, bool closed, params int[] numbers)
        {
            var db = Connect();
            foreach (var number in numbers)
            {
                db.Issues.Add(new EfHatchIssue
                {
                    ProjectId = projectId,
                    Number = number,
                    Type = "story",
                    Title = $"issue {number}",
                    Description = "",
                    StatusId = closed ? DoneId : TodoId,
                    Rank = 1024 * number,
                    CreatedBy = "operator",
                    CreatedAt = Now,
                    UpdatedAt = Now,
                    ClosedAt = closed ? Now : null,
                });
            }

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// The lookup as a caller. <paramref name="narrowedTo"/> is the console's
        /// <c>projectId</c>; <paramref name="scopedTo"/> is the caller's own project
        /// scope, which the context's query filters apply.
        /// </summary>
        public async Task<GoToDto> GoToAsync(
            string? q, int? narrowedTo = null, int limit = 50, int? scopedTo = null)
        {
            HatchContext db;
            if (scopedTo is { } scoped)
            {
                db = ScopeSeed.Request(options, ScopeSeed.Only(scoped));
                open.Add(db);
            }
            else db = Connect();

            List<int>? projectIds = narrowedTo is { } n ? [n] : null;
            var answer = await new GoToController(db).GetGoTo(q, projectIds, limit, default);
            return answer.Value ?? throw new InvalidOperationException("expected rows, got a refusal");
        }
    }
}

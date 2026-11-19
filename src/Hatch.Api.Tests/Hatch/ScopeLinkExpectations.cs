using System.Text.Json;
using Hatch.Api.Modules.Hatch;
using Microsoft.EntityFrameworkCore;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// What a reader is told about a parent link with a hidden side, read through
/// the one projection every issue goes out by. Shared by the in-memory and the
/// Postgres lane, over the seed's links: the second issue of each project hangs
/// under the first of the other, so each has a parent and a child the other
/// project's reader cannot see.
/// </summary>
internal static class ScopeLinkExpectations
{
    private static async Task<Dictionary<string, global::Hatch.Contracts.IssueDto>> ReadAsync(
        Microsoft.EntityFrameworkCore.DbContextOptions<HatchContext> options, VisibleProjects visible)
    {
        await using var db = ScopeSeed.Request(options, visible);
        var dtos = await IssueProjection.ToDtosAsync(
            db, new StubActorDirectory(), await db.Issues.OrderBy(i => i.Id).ToListAsync(),
            TestClaims.With(), DateTimeOffset.UnixEpoch, default);

        return dtos.Values.ToDictionary(d => d.Key);
    }

    public static async Task AssertAsync(DbContextOptions<HatchContext> options, ScopeSeed seed)
    {
        // Only A: AAA-2 has a parent it cannot see, AAA-1 has a child it cannot see.
        var a = await ReadAsync(options, ScopeSeed.Only(seed.ProjectA));
        Assert.Equal(["AAA-1", "AAA-2"], a.Keys.Order());
        Assert.Null(a["AAA-2"].ParentKey);
        Assert.True(a["AAA-2"].ParentBeyondScope);
        Assert.False(a["AAA-2"].ChildBeyondScope);
        Assert.True(a["AAA-1"].ChildBeyondScope);
        Assert.Empty(a["AAA-1"].ChildKeys);
        Assert.False(a["AAA-1"].ParentBeyondScope);
        Assert.DoesNotContain("BBB", JsonSerializer.Serialize(a.Values, JsonSerializerOptions.Web));

        // Only B: the reverse.
        var b = await ReadAsync(options, ScopeSeed.Only(seed.ProjectB));
        Assert.Equal(["BBB-1", "BBB-2"], b.Keys.Order());
        Assert.Null(b["BBB-2"].ParentKey);
        Assert.True(b["BBB-2"].ParentBeyondScope);
        Assert.True(b["BBB-1"].ChildBeyondScope);
        Assert.Empty(b["BBB-1"].ChildKeys);
        Assert.False(b["BBB-1"].ParentBeyondScope);
        Assert.False(b["BBB-2"].ChildBeyondScope);
        Assert.DoesNotContain("AAA", JsonSerializer.Serialize(b.Values, JsonSerializerOptions.Web));

        // Everything: no flag, and the keys are shown.
        var all = await ReadAsync(options, VisibleProjects.Everything);
        Assert.All(all.Values, d =>
        {
            Assert.False(d.ParentBeyondScope);
            Assert.False(d.ChildBeyondScope);
            Assert.Empty(d.FromBeyondScope);
        });
        Assert.Equal("BBB-1", all["AAA-2"].ParentKey);
        Assert.Equal("AAA-1", all["BBB-2"].ParentKey);
        Assert.Equal(["BBB-2"], all["AAA-1"].ChildKeys);
        Assert.Equal(["AAA-2"], all["BBB-1"].ChildKeys);
    }
}

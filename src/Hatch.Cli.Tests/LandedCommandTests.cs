namespace Hatch.Cli.Tests;

/// <summary>The <c>hatch landed</c> CLI verb, against a real git checkout.</summary>
public sealed class LandedCommandTests : RepoFixture
{
    [Fact]
    public async Task Landing_a_sha_on_the_trunk_records_the_remote_sha_and_trunk()
    {
        using var h = new CliHarness();
        h.Wire.Json("PUT", "/api/issues/AER-12/landing", Fixtures.Landing());
        var head = G(_work, "rev-parse", "HEAD").Trim();

        var code = await new IssueCommands(h.Cli).LandedAsync(["AER-12", "--repo", _work], default);

        Assert.Equal(0, code);
        var put = Assert.Single(h.Wire.To("PUT", "/api/issues/AER-12/landing"));
        var body = put.Read<LandingWriteRequest>();
        Assert.Equal(head, body.Sha);
        Assert.Equal("main", body.Trunk);
        Assert.Equal(_origin, body.Remote);
        Assert.Contains("AER-12: landed on main at " + head[..7], h.Said);
    }

    [Fact]
    public async Task A_sha_not_on_the_trunk_is_refused_before_anything_is_sent()
    {
        using var h = new CliHarness();
        G(_work, "checkout", "--quiet", "-b", "other");
        File.WriteAllText(Path.Combine(_work, "other.txt"), "other");
        Commit(_work, "Not on the trunk");
        var sha = G(_work, "rev-parse", "HEAD").Trim();

        var code = await new IssueCommands(h.Cli).LandedAsync(["AER-12", sha, "--repo", _work], default);

        Assert.Equal(1, code);
        Assert.Contains("is not on main here - land it first", h.Complained);
        Assert.Empty(h.Wire.To("PUT", "/api/issues/AER-12/landing"));
    }
}

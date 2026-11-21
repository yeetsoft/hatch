namespace Hatch.Cli.Tests;

/// <summary>
/// The pure half of <c>BuildCheck</c> - the two header/hash comparisons and the
/// verdict over a <c>version</c> run's own answer. <c>RunVersionAsync</c> runs a
/// real process, and is covered by hand rather than here.
/// </summary>
public sealed class BuildCheckTests
{
    private static readonly string Sha = new('a', 40);
    private static readonly string OtherSha = new('b', 40);

    [Fact]
    public void Header_matching_the_sha_asked_for_passes()
    {
        Assert.Null(BuildCheck.Header(Sha, Sha));
    }

    [Fact]
    public void Header_from_another_build_fails_naming_the_short_sha()
    {
        var failure = BuildCheck.Header(OtherSha, Sha);

        Assert.NotNull(failure);
        Assert.Contains("another build (mid-deploy?)", failure);
        Assert.Contains(Sha[..8], failure);
    }

    [Fact]
    public void Header_missing_entirely_fails()
    {
        Assert.NotNull(BuildCheck.Header(null, Sha));
    }

    [Fact]
    public void Sha_matching_passes()
    {
        Assert.Null(BuildCheck.Sha(Sha, Sha));
    }

    [Fact]
    public void Sha_matching_case_insensitively_passes()
    {
        Assert.Null(BuildCheck.Sha(Sha.ToUpperInvariant(), Sha));
    }

    [Fact]
    public void Sha_mismatch_fails_naming_the_short_sha()
    {
        var failure = BuildCheck.Sha(OtherSha, Sha);

        Assert.NotNull(failure);
        Assert.Contains("sha256 is not the one the board gave", failure);
        Assert.Contains(Sha[..8], failure);
    }

    [Fact]
    public void Verdict_passes_when_the_report_names_the_same_revision()
    {
        var stdout = $$"""{"revision":"{{Sha}}","sequence":1,"launcher":1}""";

        Assert.Null(BuildCheck.Verdict(stdout, 0, Sha));
    }

    [Fact]
    public void Verdict_fails_when_the_report_names_a_different_revision()
    {
        var stdout = $$"""{"revision":"{{OtherSha}}","sequence":1,"launcher":1}""";

        var failure = BuildCheck.Verdict(stdout, 0, Sha);

        Assert.NotNull(failure);
        Assert.Contains("did not report revision", failure);
        Assert.Contains(Sha[..8], failure);
    }

    /// <summary>A build from before `version` existed: "no such command", exit 1.</summary>
    [Fact]
    public void Verdict_fails_on_a_non_zero_exit()
    {
        var failure = BuildCheck.Verdict("hatch: no such command \"version\"", 1, Sha);

        Assert.NotNull(failure);
        Assert.Contains("did not report revision", failure);
    }

    [Fact]
    public void Verdict_fails_on_output_that_is_not_json()
    {
        var failure = BuildCheck.Verdict("not json at all", 0, Sha);

        Assert.NotNull(failure);
        Assert.Contains("did not report revision", failure);
    }
}

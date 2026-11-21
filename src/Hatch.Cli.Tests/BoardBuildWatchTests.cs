using Hatch.Contracts;

namespace Hatch.Cli.Tests;

/// <summary>
/// When a downloaded loop follows its board to another build: at once for an
/// older one, after ten unbroken minutes for any other word.
/// </summary>
public sealed class BoardBuildWatchTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 3, 0, 0, TimeSpan.Zero);
    private static readonly string A = new('a', 40);
    private static readonly string B = new('b', 40);

    private static RunnerBoardBuildDto Named(string compare, string? revision = null, string? download = "/api/runner/download/linux-x64", string? sha = "abc") =>
        new(revision ?? A, 4127, compare, download, sha);

    private readonly List<string> said = [];

    private BoardBuildWatch Watch(BuildHold? hold = null) => new(hold, said.Add);

    [Fact]
    public void A_board_that_is_ahead_of_this_build_is_followed_at_once()
    {
        Assert.Equal(BuildVerdict.Switch, Watch().Judge(Named(RunnerBuildComparisons.Behind), true, Start));
    }

    [Fact]
    public void Any_other_word_is_followed_only_after_ten_unbroken_minutes()
    {
        var watch = Watch();

        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named(RunnerBuildComparisons.Ahead), true, Start));
        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named(RunnerBuildComparisons.Ahead), true, Start.AddMinutes(9).AddSeconds(59)));
        Assert.Equal(BuildVerdict.Switch, watch.Judge(Named(RunnerBuildComparisons.Ahead), true, Start.AddMinutes(10)));
    }

    [Theory]
    [InlineData("diverged")]
    [InlineData("sideways")]
    public void A_word_that_is_not_behind_acts_as_ahead(string word)
    {
        var watch = Watch();

        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named(word), true, Start));
        Assert.Equal(BuildVerdict.Switch, watch.Judge(Named(word), true, Start.AddMinutes(10)));
    }

    [Fact]
    public void A_current_reply_restarts_the_ten_minutes()
    {
        var watch = Watch();
        watch.Judge(Named("ahead"), true, Start);

        Assert.Equal(BuildVerdict.Stay, watch.Judge(null, true, Start.AddMinutes(5)));

        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named("ahead"), true, Start.AddMinutes(6)));
        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named("ahead"), true, Start.AddMinutes(15)));
        Assert.Equal(BuildVerdict.Switch, watch.Judge(Named("ahead"), true, Start.AddMinutes(16)));
    }

    [Fact]
    public void A_beat_with_no_answer_restarts_the_ten_minutes()
    {
        var watch = Watch();
        watch.Judge(Named("ahead"), true, Start);

        Assert.Equal(BuildVerdict.Stay, watch.Judge(null, false, Start.AddMinutes(5)));

        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named("ahead"), true, Start.AddMinutes(10)));
        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named("ahead"), true, Start.AddMinutes(19)));
        Assert.Equal(BuildVerdict.Switch, watch.Judge(Named("ahead"), true, Start.AddMinutes(20)));
    }

    [Fact]
    public void Another_revision_restarts_the_ten_minutes_at_the_moment_it_was_named()
    {
        var watch = Watch();
        watch.Judge(Named("ahead", A), true, Start);
        watch.Judge(Named("ahead", B), true, Start.AddMinutes(8));

        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named("ahead", B), true, Start.AddMinutes(12)));
        Assert.Equal(B, watch.Revision);
        Assert.Equal(Start.AddMinutes(8), watch.Since);
        Assert.Equal(BuildVerdict.Switch, watch.Judge(Named("ahead", B), true, Start.AddMinutes(18)));
    }

    [Fact]
    public void A_build_with_no_download_stays_and_says_so_once()
    {
        var watch = Watch();

        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named("behind", download: null, sha: null), true, Start));
        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named("behind", download: null, sha: null), true, Start.AddMinutes(1)));

        var line = Assert.Single(said);
        Assert.Contains(new string('a', 8), line, StringComparison.Ordinal);
        Assert.Contains("no download", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_download_with_no_hash_is_no_download()
    {
        Assert.Equal(BuildVerdict.Stay, Watch().Judge(Named("behind", sha: null), true, Start));
        Assert.Single(said);
    }

    [Fact]
    public void A_held_revision_stays_and_is_asked_for_again_once_the_hold_lapses()
    {
        var hold = new BuildHold();
        hold.Until(A, Start.AddMinutes(2));
        var watch = Watch(hold);

        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named("behind"), true, Start));
        Assert.Equal(BuildVerdict.Stay, watch.Judge(Named("behind"), true, Start.AddMinutes(1)));
        Assert.Equal(BuildVerdict.Switch, watch.Judge(Named("behind"), true, Start.AddMinutes(2)));
    }

    [Fact]
    public void A_revision_held_for_good_is_never_asked_for()
    {
        var hold = new BuildHold();
        hold.Never(A);

        Assert.Equal(BuildVerdict.Stay, Watch(hold).Judge(Named("behind"), true, Start.AddDays(30)));
    }
}

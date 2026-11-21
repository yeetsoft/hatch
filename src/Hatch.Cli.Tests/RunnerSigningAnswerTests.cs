namespace Hatch.Cli.Tests;

/// <summary>
/// What the loop tells the board about signing: one answer for the runner, the
/// worst over the checkouts it serves, with a reason that is one masked line.
/// </summary>
public sealed class RunnerSigningAnswerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T12:00:00Z");

    private static string Path(string leaf) => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "signing-answer", leaf);

    private static Runtime Serving(Harness h, params string[] leaves) =>
        h.Runtime with { Checkouts = [.. leaves.Select(l => new CheckoutEntry(Path(l), null, Standing: false))] };

    // ---- RunnerSigning.Worst, the rule both ends share ----

    [Fact]
    public void Worst_reads_fails_before_off_before_works_with_the_reason_of_the_first_failure()
    {
        Assert.Equal(("fails", "first"), RunnerSigning.Worst([("works", null), ("fails", "first"), ("off", null), ("fails", "second")]));
        Assert.Equal(("off", null), RunnerSigning.Worst([("works", null), ("off", null)]));
        Assert.Equal(("works", null), RunnerSigning.Worst([("works", null), ("works", null)]));
    }

    [Fact]
    public void Worst_is_null_when_none_said_and_skips_a_word_it_does_not_know()
    {
        Assert.Null(RunnerSigning.Worst([]));
        Assert.Null(RunnerSigning.Worst([(null, null), ("maybe", "x")]));
        Assert.Equal(("works", null), RunnerSigning.Worst([("maybe", "x"), ("works", "stale")]));
    }

    [Fact]
    public void Worst_keeps_a_reason_only_beside_fails()
    {
        Assert.Equal(("off", null), RunnerSigning.Worst([("off", "stale")]));
    }

    // ---- SigningAnswer ----

    [Fact]
    public void A_runner_with_nothing_asked_yet_has_no_answer()
    {
        using var h = new Harness();

        Assert.Null(Serving(h, "one", "two").SigningAnswer());
    }

    [Theory]
    [InlineData(Signing.Works, "works")]
    [InlineData(Signing.Off, "off")]
    public void A_single_checkout_reads_as_its_own_answer(Signing state, string word)
    {
        using var h = new Harness();
        var runtime = Serving(h, "one");
        runtime.SigningCache.Record(Path("one"), new SigningCheck(state), Now);

        Assert.Equal((word, null), runtime.SigningAnswer());
    }

    [Fact]
    public void Several_checkouts_read_as_the_worst_in_the_order_fails_then_off_then_works()
    {
        using var h = new Harness();
        var runtime = Serving(h, "a", "b", "c");

        runtime.SigningCache.Record(Path("a"), new SigningCheck(Signing.Works), Now);
        Assert.Equal(("works", null), runtime.SigningAnswer());

        runtime.SigningCache.Record(Path("b"), new SigningCheck(Signing.Off), Now);
        Assert.Equal(("off", null), runtime.SigningAnswer());

        runtime.SigningCache.Record(Path("c"), new SigningCheck(Signing.Fails, "no secret key"), Now);
        Assert.Equal(("fails", "no secret key"), runtime.SigningAnswer());
    }

    [Fact]
    public void A_later_check_that_changes_the_answer_changes_what_is_said_even_after_the_failure_is_forgotten()
    {
        using var h = new Harness();
        var runtime = Serving(h, "a");
        runtime.SigningCache.Record(Path("a"), new SigningCheck(Signing.Fails, "no secret key"), Now);
        Assert.Equal(("fails", "no secret key"), runtime.SigningAnswer());

        // Thirty minutes on the failure is no longer remembered; what was said is.
        Assert.Null(runtime.SigningCache.Failed(Path("a"), Now.AddMinutes(31)));
        Assert.Equal(("fails", "no secret key"), runtime.SigningAnswer());

        runtime.SigningCache.Record(Path("a"), new SigningCheck(Signing.Works), Now.AddMinutes(32));
        Assert.Equal(("works", null), runtime.SigningAnswer());
    }

    [Fact]
    public void A_checkout_not_asked_yet_is_skipped_rather_than_read_as_an_answer()
    {
        using var h = new Harness();
        var runtime = Serving(h, "a", "b");
        runtime.SigningCache.Record(Path("b"), new SigningCheck(Signing.Works), Now);

        Assert.Equal(("works", null), runtime.SigningAnswer());
    }

    [Fact]
    public void A_clone_made_under_the_workspace_after_start_counts_with_the_checkouts()
    {
        using var h = new Harness();
        var workspace = Path("workspace");
        var runtime = Serving(h, "a") with { Settings = h.Runtime.Settings with { Workspace = workspace } };
        runtime.SigningCache.Record(Path("a"), new SigningCheck(Signing.Works), Now);
        runtime.SigningCache.Record(System.IO.Path.Combine(workspace, "clone"), new SigningCheck(Signing.Fails, "no agent"), Now);

        Assert.Equal(("fails", "no agent"), runtime.SigningAnswer());
    }

    // ---- Fold, which `queue` reads with when it has no runtime ----

    [Fact]
    public void Fold_reads_the_worst_of_the_checks_it_is_given_and_nothing_from_none()
    {
        Assert.Null(Runtime.Fold([]));
        Assert.Equal(("works", null), Runtime.Fold([new SigningCheck(Signing.Works)]));
        Assert.Equal(("off", null), Runtime.Fold([new SigningCheck(Signing.Works), new SigningCheck(Signing.Off)]));
        Assert.Equal(
            ("fails", "no key"),
            Runtime.Fold([new SigningCheck(Signing.Off), new SigningCheck(Signing.Fails, "no key\nsecond line"), new SigningCheck(Signing.Works)]));
    }

    [Fact]
    public void Fold_masks_and_cuts_a_reason_as_the_runners_own_answer_does()
    {
        var (_, masked) = Runtime.Fold([new SigningCheck(Signing.Fails, "key hatch_ak_0123456789abcdef is locked")])!.Value;
        Assert.Equal($"key {Scrub.Marker} is locked", masked);

        var (_, cut) = Runtime.Fold([new SigningCheck(Signing.Fails, new string('x', 500))])!.Value;
        Assert.Equal(200, cut!.Length);
    }

    [Fact]
    public void A_key_in_the_reason_is_masked()
    {
        using var h = new Harness();
        var runtime = Serving(h, "a");
        runtime.SigningCache.Record(
            Path("a"), new SigningCheck(Signing.Fails, "gpg: key hatch_ak_0123456789abcdef is locked; Bearer abc.def-123"), Now);

        var (state, why) = runtime.SigningAnswer()!.Value;

        Assert.Equal("fails", state);
        Assert.Equal($"gpg: key {Scrub.Marker} is locked; {Scrub.Marker}", why);
    }

    [Fact]
    public void A_long_reason_is_cut_to_two_hundred_characters_and_not_two_hundred_and_one()
    {
        using var h = new Harness();
        var runtime = Serving(h, "a");
        runtime.SigningCache.Record(Path("a"), new SigningCheck(Signing.Fails, new string('x', 500)), Now);

        var (_, why) = runtime.SigningAnswer()!.Value;

        Assert.Equal(200, why!.Length);
    }

    [Fact]
    public void A_reason_is_one_line()
    {
        using var h = new Harness();
        var runtime = Serving(h, "a");
        runtime.SigningCache.Record(Path("a"), new SigningCheck(Signing.Fails, "first line\nsecond line"), Now);

        var (_, why) = runtime.SigningAnswer()!.Value;

        Assert.DoesNotContain('\n', why!);
    }
}

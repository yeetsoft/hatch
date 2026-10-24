namespace Hatch.Cli.Tests;

/// <summary>
/// <see cref="Program.TryExtractProfile"/> is the only testable seam for the
/// <c>--profile</c> strip: top-level statements have no entry point a test can
/// call, so the composition around it (resolve-or-skip-for-config, thread into
/// <c>Settings.Layers</c>/<c>Settings.TryLoad</c>) is reviewed by eye rather than
/// proven here - see HA-1463.
/// </summary>
public sealed class ProgramProfileTests
{
    [Fact]
    public void Pulls_the_flag_and_its_value_out_wherever_it_appears()
    {
        Assert.True(Program.TryExtractProfile(
            ["--profile", "work-laptop", "board"], out var rest1, out var profile1, out _));
        Assert.Equal(["board"], rest1);
        Assert.Equal("work-laptop", profile1);

        Assert.True(Program.TryExtractProfile(
            ["work", "--profile", "work-laptop", "AER-12"], out var rest2, out var profile2, out _));
        Assert.Equal(["work", "AER-12"], rest2);
        Assert.Equal("work-laptop", profile2);

        Assert.True(Program.TryExtractProfile(
            ["work", "AER-12", "--profile", "work-laptop"], out var rest3, out var profile3, out _));
        Assert.Equal(["work", "AER-12"], rest3);
        Assert.Equal("work-laptop", profile3);
    }

    [Fact]
    public void Leaves_args_unchanged_when_the_flag_is_absent()
    {
        string[] args = ["work", "AER-12", "--model", "opus"];

        Assert.True(Program.TryExtractProfile(args, out var rest, out var profile, out _));
        Assert.Equal(args, rest);
        Assert.Null(profile);
    }

    [Fact]
    public void A_trailing_bare_flag_refuses()
    {
        Assert.False(Program.TryExtractProfile(
            ["board", "--profile"], out _, out var profile, out var refusal));
        Assert.Null(profile);
        Assert.Equal("hatch: --profile takes a name", refusal);
    }

    [Fact]
    public void Trims_the_value()
    {
        Assert.True(Program.TryExtractProfile(
            ["--profile", "  work-laptop  ", "board"], out _, out var profile, out _));
        Assert.Equal("work-laptop", profile);
    }

    [Fact]
    public void Repeated_flag_leaves_the_last_one_standing()
    {
        Assert.True(Program.TryExtractProfile(
            ["--profile", "first", "--profile", "second", "board"], out var rest, out var profile, out _));
        Assert.Equal(["board"], rest);
        Assert.Equal("second", profile);
    }
}

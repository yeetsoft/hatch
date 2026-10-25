namespace Hatch.Cli.Tests;

public sealed class RunnerSetTests
{
    [Fact]
    public void Absent_defaults_to_one_runner_and_leaves_args_unchanged()
    {
        string[] args = ["work", "AER-12"];

        Assert.True(RunnerSet.TryExtract(args, out var rest, out var runners, out _));
        Assert.Equal(args, rest);
        Assert.Equal(1, runners);
    }

    [Fact]
    public void Pulls_the_flag_and_its_value_out_wherever_it_appears()
    {
        Assert.True(RunnerSet.TryExtract(["--runners", "4", "go-to-work"], out var rest1, out var runners1, out _));
        Assert.Equal(["go-to-work"], rest1);
        Assert.Equal(4, runners1);

        Assert.True(RunnerSet.TryExtract(["go-to-work", "--runners", "4", "--once"], out var rest2, out var runners2, out _));
        Assert.Equal(["go-to-work", "--once"], rest2);
        Assert.Equal(4, runners2);

        Assert.True(RunnerSet.TryExtract(["go-to-work", "--once", "--runners", "4"], out var rest3, out var runners3, out _));
        Assert.Equal(["go-to-work", "--once"], rest3);
        Assert.Equal(4, runners3);
    }

    [Fact]
    public void Repeated_flag_leaves_the_last_one_standing()
    {
        Assert.True(RunnerSet.TryExtract(["--runners", "4", "--runners", "8"], out var rest, out var runners, out _));
        Assert.Equal([], rest);
        Assert.Equal(8, runners);
    }

    [Fact]
    public void Trims_the_value()
    {
        Assert.True(RunnerSet.TryExtract(["--runners", "  4  "], out _, out var runners, out _));
        Assert.Equal(4, runners);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void Boundary_values_are_accepted(int value)
    {
        Assert.True(RunnerSet.TryExtract(["--runners", value.ToString()], out _, out var runners, out _));
        Assert.Equal(value, runners);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("17")]
    [InlineData("not-a-number")]
    public void Out_of_range_or_unparseable_values_refuse(string value)
    {
        Assert.False(RunnerSet.TryExtract(["--runners", value], out _, out _, out var refusal));
        Assert.Equal("hatch: --runners takes a count from 1 to 16", refusal);
    }

    [Fact]
    public void A_trailing_bare_flag_refuses_with_the_same_message()
    {
        Assert.False(RunnerSet.TryExtract(["go-to-work", "--runners"], out _, out _, out var refusal));
        Assert.Equal("hatch: --runners takes a count from 1 to 16", refusal);
    }

    [Fact]
    public void Workspace_for_slot_one_is_unchanged()
    {
        Assert.Equal("/home/runner/workspace", RunnerSet.WorkspaceFor("/home/runner/workspace", 1));
    }

    [Fact]
    public void Workspace_for_slot_two_with_no_trailing_separator_appends_dot_n()
    {
        Assert.Equal("/home/runner/workspace.2", RunnerSet.WorkspaceFor("/home/runner/workspace", 2));
    }

    [Fact]
    public void Workspace_for_slot_two_trims_a_trailing_separator_first()
    {
        Assert.Equal("/home/runner/workspace.2", RunnerSet.WorkspaceFor("/home/runner/workspace/", 2));
        Assert.Equal("/home/runner/workspace.2", RunnerSet.WorkspaceFor("/home/runner/workspace\\", 2));
    }

    [Fact]
    public void Workspace_for_a_higher_slot_suffixes_correctly()
    {
        Assert.Equal("/home/runner/workspace.16", RunnerSet.WorkspaceFor("/home/runner/workspace", 16));
    }

    [Fact]
    public void Workspace_for_appends_the_same_way_regardless_of_shape()
    {
        Assert.Equal(
            "/home/runner/workspaces/night-shift.2",
            RunnerSet.WorkspaceFor("/home/runner/workspaces/night-shift", 2));
        Assert.Equal(
            "/home/runner/workspace.2",
            RunnerSet.WorkspaceFor("/home/runner/workspace", 2));
    }

    [Fact]
    public void Night_state_null_stays_null()
    {
        Assert.Null(RunnerSet.NightStateFor(null, 1));
        Assert.Null(RunnerSet.NightStateFor(null, 2));
    }

    [Fact]
    public void Night_state_slot_one_still_gets_a_suffix()
    {
        Assert.Equal("/tmp/state.1", RunnerSet.NightStateFor("/tmp/state", 1));
    }

    [Fact]
    public void Night_state_for_a_higher_slot_suffixes_correctly()
    {
        Assert.Equal("/tmp/state.16", RunnerSet.NightStateFor("/tmp/state", 16));
    }

    [Fact]
    public void Args_for_slot_one_with_nothing_already_present_just_appends_no_keys()
    {
        string[] rest = ["go-to-work", "--once"];
        Assert.Equal(["go-to-work", "--once", "--no-keys"], RunnerSet.ArgsFor(rest, 1, "/home/runner/workspace"));
    }

    [Fact]
    public void Args_for_slot_one_leaves_its_own_repo_and_workspace_untouched()
    {
        string[] rest = ["go-to-work", "--repo", "/a", "--workspace", "/b"];
        Assert.Equal(
            ["go-to-work", "--repo", "/a", "--workspace", "/b", "--no-keys"],
            RunnerSet.ArgsFor(rest, 1, "/home/runner/workspace"));
    }

    [Fact]
    public void Args_for_slot_two_strips_repo_and_workspace_and_appends_its_own()
    {
        string[] rest = ["go-to-work", "--repo", "/a", "--workspace", "/b", "--once"];
        Assert.Equal(
            ["go-to-work", "--once", "--workspace", "/home/runner/workspace.2", "--no-keys"],
            RunnerSet.ArgsFor(rest, 2, "/home/runner/workspace.2"));
    }

    [Fact]
    public void Args_for_slot_two_strips_every_repeated_repo_pair()
    {
        string[] rest = ["go-to-work", "--repo", "/a", "--repo", "/b", "--once"];
        Assert.Equal(
            ["go-to-work", "--once", "--workspace", "/home/runner/workspace.2", "--no-keys"],
            RunnerSet.ArgsFor(rest, 2, "/home/runner/workspace.2"));
    }

    [Fact]
    public void Args_for_slot_two_with_neither_flag_present_just_appends()
    {
        string[] rest = ["go-to-work", "--once"];
        Assert.Equal(
            ["go-to-work", "--once", "--workspace", "/home/runner/workspace.2", "--no-keys"],
            RunnerSet.ArgsFor(rest, 2, "/home/runner/workspace.2"));
    }

    [Fact]
    public void Fold_of_nothing_or_all_zero_is_zero()
    {
        Assert.Equal(0, RunnerSet.Fold([], false));
        Assert.Equal(0, RunnerSet.Fold([0, 0], false));
    }

    [Fact]
    public void Fold_with_any_one_is_one()
    {
        Assert.Equal(1, RunnerSet.Fold([0, 1, 0], false));
    }

    [Fact]
    public void Fold_with_a_restart_and_not_stopped_is_the_restart_code()
    {
        Assert.Equal(75, RunnerSet.Fold([0, 75, 0], false));
    }

    [Fact]
    public void Fold_with_a_restart_alone_and_stopped_is_zero()
    {
        Assert.Equal(0, RunnerSet.Fold([0, 75, 0], true));
    }

    [Fact]
    public void Fold_with_a_restart_and_a_one_and_stopped_is_one()
    {
        Assert.Equal(1, RunnerSet.Fold([75, 1], true));
    }

    [Fact]
    public void Fold_with_a_restart_and_a_one_and_not_stopped_is_the_restart_code()
    {
        Assert.Equal(75, RunnerSet.Fold([75, 1], false));
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData(" 3 ", 3)]
    [InlineData("16", 16)]
    public void A_slot_of_two_or_more_is_a_member(string value, int expected) =>
        Assert.Equal(expected, RunnerSet.SlotFrom(new Dictionary<string, string?> { [RunnerSet.SlotVariable] = value }));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("two")]
    public void Anything_else_is_an_ordinary_runner(string? value) =>
        Assert.Null(RunnerSet.SlotFrom(new Dictionary<string, string?> { [RunnerSet.SlotVariable] = value }));

    [Fact]
    public void No_slot_variable_is_an_ordinary_runner() =>
        Assert.Null(RunnerSet.SlotFrom(new Dictionary<string, string?>()));

    [Fact]
    public void A_member_keeps_its_origin_and_workspace_and_loses_its_checkouts_and_name()
    {
        var settings = new Settings
        {
            Repos = ["/a"], Runner = "Fry", Base = "https://example.test", Key = "k", Workspace = "/w",
        };

        var member = RunnerSet.ForMember(settings);

        Assert.Empty(member.Repos);
        Assert.Null(member.Runner);
        Assert.Equal("https://example.test", member.Base);
        Assert.Equal("/w", member.Workspace);
    }

    [Fact]
    public void The_workspace_handed_over_is_the_last_one_given()
    {
        Assert.Null(RunnerSet.WorkspaceFrom(["--once"]));
        Assert.Equal("/a", RunnerSet.WorkspaceFrom(["--once", "--workspace", "/a"]));
        Assert.Equal("/b", RunnerSet.WorkspaceFrom(["--workspace", "/a", "--once", "--workspace", "/b"]));
        Assert.Null(RunnerSet.WorkspaceFrom(["--once", "--workspace"]));
    }
}

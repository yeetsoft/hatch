namespace Hatch.Cli.Tests;

/// <summary>
/// <c>work KEY</c> and <c>work --dry-run</c>: a row pinned to another agent's
/// family refuses before anything is claimed or spawned, and <c>--model</c> -
/// the flag beating the playbook, as everywhere else "effective" means that -
/// decides it as much as the playbook does.
/// </summary>
/// <remarks>
/// This runner is always <c>claude</c> here, never the other family in
/// <see cref="TwoAgents"/> - <see cref="Agents.Resolve"/> only knows the ids in
/// <see cref="Settings.KnownAgentIds"/>, so a
/// <see cref="Runtime"/> built with <c>Settings.Agent</c> set to anything else
/// still refuses at <see cref="WorkCommand.RunAsync"/>'s own agent resolve
/// before ever reaching a ticket. The mismatch this exercises instead is the
/// pinned row belonging to the other family, which is the same comparison
/// <see cref="Picker.WrongAgent"/> makes either way round.
/// </remarks>
public sealed class WorkModelFamilyTests
{
    /// <summary>Claude's family and one more, so a row can be pinned to an agent this runner is not.</summary>
    private static readonly ModelFamilies TwoAgents = new([
        ModelFamilies.Default.Rows[0], new ModelFamily("fake-agent", "^fake-[a-z0-9-]+$", "fake-1")]);

    private static WorkDto PinnedTo(string model) =>
        Fixtures.Work("AER-1") with { Playbook = Fixtures.Playbook(model: model) };

    [Fact]
    public async Task A_named_row_pinned_to_another_agents_family_refuses_and_claims_nothing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", PinnedTo("fake-1"));

        var code = await new WorkCommand(h.Runtime with { Families = TwoAgents }).RunAsync(["AER-1"], default);

        Assert.Equal(2, code);
        Assert.Contains(h.Say.Complained, l => l.Contains(
            "AER-1 - pinned to fake-1, a fake-agent model - this runner's agent is claude", StringComparison.Ordinal));
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_dry_run_on_a_row_pinned_to_another_agents_family_refuses_and_prints_no_prompt()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", PinnedTo("fake-1"));

        var code = await new WorkCommand(h.Runtime with { Families = TwoAgents }).RunAsync(["AER-1", "--dry-run"], default);

        Assert.Equal(2, code);
        Assert.Contains(h.Say.Complained, l => l.Contains(
            "AER-1 - pinned to fake-1, a fake-agent model - this runner's agent is claude", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("## The ticket", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_flag_naming_an_alias_beats_a_foreign_pinned_playbook()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", PinnedTo("fake-1"));

        var code = await new WorkCommand(h.Runtime with { Families = TwoAgents })
            .RunAsync(["AER-1", "--dry-run", "--model", "opus"], default);

        Assert.Equal(0, code);
        Assert.Contains(h.Say.Said, l => l.Contains("## The ticket", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_model_flag_naming_a_foreign_pinned_id_refuses_even_over_an_alias_playbook()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", PinnedTo("opus"));

        var code = await new WorkCommand(h.Runtime with { Families = TwoAgents })
            .RunAsync(["AER-1", "--dry-run", "--model", "fake-1"], default);

        Assert.Equal(2, code);
        Assert.Contains(h.Say.Complained, l => l.Contains(
            "AER-1 - pinned to fake-1, a fake-agent model - this runner's agent is claude", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_claude_row_proceeds_under_the_default_agent_as_today()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", PinnedTo("claude-opus-5"));

        var code = await new WorkCommand(h.Runtime with { Families = TwoAgents }).RunAsync(["AER-1", "--dry-run"], default);

        Assert.Equal(0, code);
        Assert.Contains(h.Say.Said, l => l.Contains("## The ticket", StringComparison.Ordinal));
    }
}

namespace Hatch.Cli.Tests;

/// <summary>
/// <c>work KEY</c> and <c>work --dry-run</c>: a row pinned to a model its
/// adapter's server has not pulled refuses before anything is claimed or
/// spawned - the sibling of <see cref="WorkDeniedToolsTests"/> for the third
/// reason <see cref="Picker.Pick.Skipped"/> exists.
/// </summary>
public sealed class WorkCanServeTests
{
    private const string Refusal =
        "pinned to qwen3-coder:30b, which this runner's ollama has not pulled - ollama pull qwen3-coder:30b would";

    [Fact]
    public async Task A_named_row_whose_model_the_adapter_cannot_serve_refuses_and_claims_nothing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        var runtime = h.Runtime with { Adapter = new FakeAdapter { CanServeRefusal = Refusal } };

        var code = await new WorkCommand(runtime).RunAsync(["AER-1"], default);

        Assert.Equal(2, code);
        Assert.Contains(h.Say.Complained, l => l.Contains($"AER-1 - {Refusal}", StringComparison.Ordinal));
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
        Assert.Empty(h.Sessions.Spawned);
    }

    [Fact]
    public async Task A_dry_run_on_a_row_whose_model_the_adapter_cannot_serve_refuses_and_prints_no_prompt()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));
        var runtime = h.Runtime with { Adapter = new FakeAdapter { CanServeRefusal = Refusal } };

        var code = await new WorkCommand(runtime).RunAsync(["AER-1", "--dry-run"], default);

        Assert.Equal(2, code);
        Assert.Contains(h.Say.Complained, l => l.Contains($"AER-1 - {Refusal}", StringComparison.Ordinal));
        Assert.DoesNotContain(h.Say.Said, l => l.Contains("## The ticket", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_row_proceeds_under_the_default_adapter_unchanged()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1"));

        var code = await new WorkCommand(h.Runtime).RunAsync(["AER-1", "--dry-run"], default);

        Assert.Equal(0, code);
        Assert.Contains(h.Say.Said, l => l.Contains("## The ticket", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_hop_row_passes_through_even_under_an_adapter_that_refuses_every_model()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/hatch/work/AER-1", Fixtures.Work("AER-1", hop: true));
        h.Wire.Json("POST", "/api/hatch/work/AER-1/hop", Fixtures.Issue("AER-1"));
        var runtime = h.Runtime with { Adapter = new FakeAdapter { CanServeRefusal = Refusal } };

        var code = await new WorkCommand(runtime).RunAsync(["AER-1"], default);

        Assert.Equal(0, code);
        Assert.Empty(h.Wire.To("POST", "/api/hatch/issues/AER-1/claim"));
    }
}

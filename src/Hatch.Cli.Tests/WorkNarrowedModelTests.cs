namespace Hatch.Cli.Tests;

/// <summary>
/// <c>work KEY</c>: a row whose model the runner has stopped offering refuses
/// before anything is claimed or spawned - the named read's side of
/// <see cref="Picker.Pick.Skipped"/> for a narrowed model.
/// </summary>
public sealed class WorkNarrowedModelTests
{
    private const string Narrowed =
        "runs on opus - this runner's claude no longer offers it for the rest of this run";

    [Fact]
    public async Task A_named_row_whose_model_is_narrowed_refuses_and_claims_nothing()
    {
        using var h = new Harness();
        h.Wire.Json("GET", "/api/work/AER-1", Fixtures.Work("AER-1"));
        h.Runtime.Narrowing.Narrow("opus", null, DateTimeOffset.UtcNow);

        var code = await new WorkCommand(h.Runtime).RunAsync(["AER-1"], default);

        Assert.Equal(2, code);
        Assert.Contains(h.Say.Complained, l => l.Contains($"AER-1 - {Narrowed}", StringComparison.Ordinal));
        Assert.Empty(h.Wire.To("POST", "/api/issues/AER-1/claim"));
        Assert.Empty(h.Sessions.Spawned);
    }
}

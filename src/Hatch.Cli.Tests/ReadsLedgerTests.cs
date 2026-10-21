namespace Hatch.Cli.Tests;

/// <summary>
/// What counts as a read, and what never does: the recognizers keep free text out
/// of the ledger, and the ranking keeps both ends dropping the same path.
/// </summary>
public sealed class ReadsLedgerTests
{
    [Theory]
    [InlineData("hatch show HA-1186", "hatch show")]
    [InlineData("./scripts/hatch.sh show HA-1186", "hatch show")]
    [InlineData("scripts/hatch.sh show HA-1186", "hatch show")]
    [InlineData(@".\scripts\hatch.ps1 show HA-1186", "hatch show")]
    [InlineData("HATCH show x", "hatch show")]
    [InlineData("'hatch' \"show\" HA-1", "hatch show")]
    [InlineData("hatch show; rm -rf /", "hatch show")]
    [InlineData("hatch comments HA-1186", "hatch comments")]
    [InlineData("hatch api GET /api/hatch/issues/HA-1186/events?x=1", "/api/hatch/issues/HA-1186/events")]
    [InlineData("hatch api GET /api/hatch/plan#top", "/api/hatch/plan")]
    [InlineData("hatch api GET '/api/hatch/plan?x=1'", "/api/hatch/plan")]
    [InlineData("hatch api POST /api/hatch/issues {\"title\":\"x\"}", "/api/hatch/issues")]
    [InlineData("powershell -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\hatch.ps1 api GET /x?y=1", "/x")]
    [InlineData("pwsh -File scripts\\hatch.ps1 queue", "hatch queue")]
    [InlineData("pwsh.exe -File C:\\work\\Hatch.PS1 board", "hatch board")]
    [InlineData("POWERSHELL.EXE -File x/hatch.ps1 next", "hatch next")]
    public void A_hatch_call_is_keyed_on_its_verb_or_its_endpoint(string command, string expected) =>
        Assert.Equal(expected, ReadsLedger.FromCommand(command));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("cat secrets.txt")]
    [InlineData("hatchet show")]
    [InlineData("bash scripts/hatch.sh show")]
    [InlineData("cd x && hatch show")]
    [InlineData("hatch")]
    [InlineData("hatch --mine queue")]
    [InlineData("hatch ./show")]
    [InlineData("hatch Show")]
    [InlineData("hatch api")]
    [InlineData("hatch api GET")]
    [InlineData("hatch api GET api/hatch/issues")]
    [InlineData("hatch api GET /a/$(whoami)")]
    [InlineData("hatch api GET /a;rm")]
    [InlineData("pwsh -NoProfile -Command Get-Date")]
    public void Anything_else_is_not_counted(string? command) =>
        Assert.Null(ReadsLedger.FromCommand(command));

    [Theory]
    [InlineData("https://example.test/docs/page", "https://example.test/docs/page")]
    [InlineData("https://u:pw@host/path?x#y", "https://host/path")]
    [InlineData("http://host:8080/a/b?x=1", "http://host:8080/a/b")]
    [InlineData("https://host:443/a", "https://host/a")]
    public void A_fetched_url_is_kept_to_its_scheme_host_and_path(string url, string expected) =>
        Assert.Equal(expected, ReadsLedger.FromUrl(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://host/x")]
    public void A_url_that_is_not_http_is_not_counted(string? url) =>
        Assert.Null(ReadsLedger.FromUrl(url));

    [Fact]
    public void Blank_paths_are_ignored()
    {
        var ledger = new ReadsLedger();

        ledger.Add(null);
        ledger.Add("");
        ledger.Add("  ");

        Assert.Empty(ledger.Ranked());
    }

    [Fact]
    public void Ranked_puts_the_heaviest_first_and_breaks_ties_by_path_ordinally()
    {
        var ledger = new ReadsLedger();
        foreach (var path in (string[])["b", "a", "B", "c", "c", "c", "b"])
            ledger.Add(path);

        Assert.Equal(
            [("c", 3), ("b", 2), ("B", 1), ("a", 1)],
            ledger.Ranked().Select(r => (r.Path, r.Count)));
    }

    [Fact]
    public void Ranked_keeps_the_heaviest_fifty_and_drops_the_lightest()
    {
        var ledger = new ReadsLedger();
        for (var i = 0; i < 51; i++)
            for (var n = 0; n <= i; n++)
                ledger.Add($"src/file{i:D2}.cs");

        var ranked = ledger.Ranked();

        Assert.Equal(ReadsLedger.Max, ranked.Count);
        Assert.Equal(51, ranked[0].Count);
        Assert.DoesNotContain(ranked, r => r.Path == "src/file00.cs");
    }
}

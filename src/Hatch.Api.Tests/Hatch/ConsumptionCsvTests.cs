using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// <see cref="ConsumptionCsv.Write"/>: a pure writer over
/// <see cref="ConsumptionReport.Row"/>s, no database and no server.
/// </summary>
public class ConsumptionCsvTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);

    private static ConsumptionReport.Row Row(
        string title = "a row", string? parentKey = null,
        int unpricedSessions = 0, int sessions = 1, decimal costUsd = 1.5m) =>
        new(
            "AER-1", title, "epic", "AER", 10, parentKey,
            new WorkLogTotalsDto(sessions, 0, 100, 200, 10, 20, 330, costUsd, unpricedSessions, 5_000),
            At, At.AddHours(1));

    private static string[] Records(string csv) =>
        csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void TheHeader_IsTheEighteenColumns_InOrder()
    {
        Assert.Equal(
            [
                "key", "title", "type", "project", "statusId", "parentKey",
                "sessions", "errors", "inputTokens", "outputTokens", "cacheCreationTokens", "cacheReadTokens",
                "totalTokens", "costUsd", "unpricedSessions", "runnerMs", "firstSessionAt", "lastSessionAt",
            ],
            ConsumptionCsv.Header);

        var records = Records(ConsumptionCsv.Write([]));
        Assert.Equal(1, records.Length);
        Assert.Equal(string.Join(',', ConsumptionCsv.Header), records[0]);
    }

    [Fact]
    public void ATitleWithACommaAndAQuote_ComesBackQuoted_WithTheQuoteDoubled()
    {
        var csv = ConsumptionCsv.Write([Row(title: "fix \"the\" bug, quickly")]);

        var records = Records(csv);
        Assert.Equal(2, records.Length);
        Assert.Contains("\"fix \"\"the\"\" bug, quickly\"", records[1]);
    }

    [Fact]
    public void ATitleWithANewline_StaysInsideOneRecord()
    {
        var csv = ConsumptionCsv.Write([Row(title: "two\nlines")]);

        var records = Records(csv);
        Assert.Equal(2, records.Length);
        Assert.Contains("\"two\nlines\"", records[1]);
    }

    [Fact]
    public void EveryDataRow_HasTheHeadersFieldCount()
    {
        var csv = ConsumptionCsv.Write([Row(), Row(parentKey: "AER-2")]);

        var records = Records(csv);
        Assert.Equal(3, records.Length);
        foreach (var record in records.Skip(1))
            Assert.Equal(ConsumptionCsv.Header.Length, record.Split(',').Length);
    }

    [Fact]
    public void CostUsd_IsEmpty_WhenEveryRowsSessionIsUnpriced()
    {
        var csv = ConsumptionCsv.Write([Row(unpricedSessions: 1, sessions: 1, costUsd: 0m)]);

        var fields = Records(csv)[1].Split(',');
        Assert.Equal("", fields[13]);
    }

    [Fact]
    public void CostUsd_IsAPlainDecimal_WhenAnySessionIsPriced()
    {
        var csv = ConsumptionCsv.Write([Row(unpricedSessions: 0, sessions: 2, costUsd: 3.25m)]);

        var fields = Records(csv)[1].Split(',');
        Assert.Equal("3.25", fields[13]);
    }

    [Fact]
    public void ParentKey_IsEmpty_WhenTheRowHasNone()
    {
        var csv = ConsumptionCsv.Write([Row(parentKey: null)]);

        var fields = Records(csv)[1].Split(',');
        Assert.Equal("", fields[5]);
    }

    [Fact]
    public void ParentKey_IsItself_WhenTheRowHasOne()
    {
        var csv = ConsumptionCsv.Write([Row(parentKey: "AER-2")]);

        var fields = Records(csv)[1].Split(',');
        Assert.Equal("AER-2", fields[5]);
    }
}

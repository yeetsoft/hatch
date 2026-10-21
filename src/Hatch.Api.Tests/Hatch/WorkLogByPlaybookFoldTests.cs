using Hatch.Api.Modules.Hatch;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The by-playbook fold, called directly over hand-built rows - no database, no
/// controller. What it groups on and what it adds up.
/// </summary>
public class WorkLogByPlaybookFoldTests
{
    private static WorkLogRollup.ByPlaybookRow Row(
        string? key = "7",
        int? from = 1,
        int? to = 2,
        string? type = "task",
        string? model = "opus",
        string? effort = "high",
        bool isError = false,
        bool described = true,
        int turns = 1,
        long input = 0,
        long output = 0,
        long cacheCreation = 0,
        long cacheRead = 0,
        decimal? usd = 0m,
        long? peak = null,
        int? promptChars = null,
        string? reads = null) => new(
            key, from, to, type, model, effort, isError, described, turns,
            input, output, cacheCreation, cacheRead, usd, peak, promptChars, reads);

    private static WorkLogRollup.ByPlaybookRow Unknown(long input = 0, decimal? usd = 0m) =>
        Row(key: null, from: null, to: null, type: null, model: null, effort: null, input: input, usd: usd);

    [Fact]
    public void NoRows_FoldToNoGroups() =>
        Assert.Empty(WorkLogRollup.FoldByPlaybook([]));

    [Fact]
    public void SessionsSharingTheSixFields_FoldIntoOneGroup()
    {
        var rows = WorkLogRollup.FoldByPlaybook([Row(), Row(), Row()]);

        Assert.Equal(3, Assert.Single(rows).Sessions);
    }

    [Fact]
    public void EachOfTheSixFields_SplitsAGroupOnItsOwn()
    {
        var rows = WorkLogRollup.FoldByPlaybook([
            Row(),
            Row(key: "8"),
            Row(from: 3),
            Row(to: 4),
            Row(type: "bug"),
            Row(model: "sonnet"),
            Row(effort: "low"),
        ]);

        Assert.Equal(7, rows.Count);
        Assert.All(rows, r => Assert.Equal(1, r.Sessions));
    }

    [Fact]
    public void EveryRowAnOlderRunnerWrote_FoldsIntoOneUnknownGroup()
    {
        var rows = WorkLogRollup.FoldByPlaybook([Unknown(input: 10), Unknown(input: 20), Unknown(input: 30), Row()]);

        Assert.Equal(2, rows.Count);
        var unknown = Assert.Single(rows, r => r.PlaybookKey is null);
        Assert.Equal(3, unknown.Sessions);
        Assert.Equal(60, unknown.InputTokens);
        Assert.Null(unknown.FromStatusId);
        Assert.Null(unknown.ToStatusId);
        Assert.Null(unknown.IssueType);
        Assert.Null(unknown.Model);
        Assert.Null(unknown.Effort);
    }

    [Fact]
    public void TurnsAndTheFourTokenCounts_Sum_AndTotalTokensIsTheirSum()
    {
        var row = Assert.Single(WorkLogRollup.FoldByPlaybook([
            Row(turns: 3, input: 1, output: 20, cacheCreation: 300, cacheRead: 4_000),
            Row(turns: 5, input: 10, output: 200, cacheCreation: 3_000, cacheRead: 40_000),
        ]));

        Assert.Equal(8, row.Turns);
        Assert.Equal(11, row.InputTokens);
        Assert.Equal(220, row.OutputTokens);
        Assert.Equal(3_300, row.CacheCreationTokens);
        Assert.Equal(44_000, row.CacheReadTokens);
        Assert.Equal(47_531, row.TotalTokens);
    }

    [Fact]
    public void ErrorsAndUndescribed_AreCountedPerGroup()
    {
        var row = Assert.Single(WorkLogRollup.FoldByPlaybook([
            Row(isError: true, described: false),
            Row(isError: true),
            Row(described: false),
            Row(),
        ]));

        Assert.Equal(4, row.Sessions);
        Assert.Equal(2, row.Errors);
        Assert.Equal(2, row.Undescribed);
    }

    [Fact]
    public void Cost_SumsThePricedRowsOnly_AndTheRestAreCountedUnpriced()
    {
        var row = Assert.Single(WorkLogRollup.FoldByPlaybook([
            Row(usd: 1.25m),
            Row(usd: null),
            Row(usd: 0.50m),
            Row(usd: null),
        ]));

        Assert.Equal(1.75m, row.CostUsd);
        Assert.Equal(2, row.UnpricedSessions);
    }

    [Fact]
    public void AMedianOfTwoValues_IsTheirMean_AndOfThreeIsTheMiddleOne()
    {
        var two = Assert.Single(WorkLogRollup.FoldByPlaybook([
            Row(peak: 100, promptChars: 10),
            Row(peak: 201, promptChars: 21),
        ]));
        Assert.Equal(150.5, two.MedianPeakContextTokens);
        Assert.Equal(15.5, two.MedianPromptChars);

        // Deliberately unsorted: the fold sorts, the caller does not have to.
        var three = Assert.Single(WorkLogRollup.FoldByPlaybook([
            Row(peak: 900, promptChars: 90),
            Row(peak: 100, promptChars: 10),
            Row(peak: 500, promptChars: 50),
        ]));
        Assert.Equal(500, three.MedianPeakContextTokens);
        Assert.Equal(50, three.MedianPromptChars);
    }

    [Fact]
    public void MaxPeakContext_IgnoresNulls_AndTheMedianIsOverTheReportedValuesOnly()
    {
        var row = Assert.Single(WorkLogRollup.FoldByPlaybook([
            Row(peak: null),
            Row(peak: 70_000),
            Row(peak: null),
            Row(peak: 264_000),
        ]));

        Assert.Equal(264_000, row.MaxPeakContextTokens);
        Assert.Equal(167_000, row.MedianPeakContextTokens);
    }

    [Fact]
    public void AGroupWhereEveryRowIsNullForAMetric_ReportsNullForIt()
    {
        var row = Assert.Single(WorkLogRollup.FoldByPlaybook([Row(), Row()]));

        Assert.Null(row.MedianPeakContextTokens);
        Assert.Null(row.MaxPeakContextTokens);
        Assert.Null(row.MedianPromptChars);
    }

    [Fact]
    public void AMetricIsFoldedOverItsOwnGroup_NotTheWholeWindow()
    {
        var rows = WorkLogRollup.FoldByPlaybook([
            Row(model: "opus", peak: 1_000),
            Row(model: "sonnet", peak: null),
        ]);

        Assert.Equal(1_000, Assert.Single(rows, r => r.Model == "opus").MaxPeakContextTokens);
        Assert.Null(Assert.Single(rows, r => r.Model == "sonnet").MaxPeakContextTokens);
    }

    [Fact]
    public void Rows_ComeHeaviestFirst_AndTiesBreakOnTheGroupingFields()
    {
        var rows = WorkLogRollup.FoldByPlaybook([
            Row(key: "b", input: 100),
            Row(key: "a", input: 100),
            Row(key: "heavy", input: 5_000),
            Row(key: "light", input: 1),
        ]);

        Assert.Equal(["heavy", "a", "b", "light"], rows.Select(r => r.PlaybookKey));
    }

    [Fact]
    public void TheFoldLeavesTheStatusNamesForTheCallerToResolve()
    {
        var row = Assert.Single(WorkLogRollup.FoldByPlaybook([Row(from: 1, to: 2)]));

        Assert.Equal(1, row.FromStatusId);
        Assert.Equal(2, row.ToStatusId);
        Assert.Null(row.FromStatusName);
        Assert.Null(row.ToStatusName);
    }
}

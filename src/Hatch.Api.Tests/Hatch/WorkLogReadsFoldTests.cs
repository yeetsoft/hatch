using Hatch.Api.Modules.Hatch;
using Hatch.Contracts;

namespace Hatch.Api.Tests.Hatch;

/// <summary>
/// The reads fold, called directly over hand-built stored columns - no
/// database, no controller. What it sums and how it ranks.
/// </summary>
public class WorkLogReadsFoldTests
{
    private static string Stored(params (string Path, int Count)[] reads) =>
        WorkLogRollup.WriteReads(reads.Select(r => new WorkLogReadDto(r.Path, r.Count)).ToList())!;

    [Fact]
    public void NoRows_FoldToNoReads() =>
        Assert.Empty(WorkLogRollup.FoldReads([]));

    [Fact]
    public void AllNullOrMissingReadsColumns_FoldToNoReads() =>
        Assert.Empty(WorkLogRollup.FoldReads([null, null]));

    [Fact]
    public void CountsForTheSamePath_SumAcrossRows()
    {
        var reads = WorkLogRollup.FoldReads([
            Stored(("a.cs", 3)),
            Stored(("a.cs", 2)),
        ]);

        Assert.Equal(new WorkLogReadDto("a.cs", 5), Assert.Single(reads));
    }

    [Fact]
    public void Reads_ComeHeaviestCountFirst_AndTiesBreakOnPathAscending()
    {
        var reads = WorkLogRollup.FoldReads([
            Stored(("b.cs", 5), ("a.cs", 5), ("heavy.cs", 100), ("light.cs", 1)),
        ]);

        Assert.Equal(["heavy.cs", "a.cs", "b.cs", "light.cs"], reads.Select(r => r.Path));
    }

    [Fact]
    public void ARowWithNoReadsColumn_ContributesNothing()
    {
        var reads = WorkLogRollup.FoldReads([
            Stored(("a.cs", 1)),
            null,
        ]);

        Assert.Equal(new WorkLogReadDto("a.cs", 1), Assert.Single(reads));
    }

    [Fact]
    public void MoreThanTheCap_IsCutAtMaxTopReads_KeepingTheHeaviest()
    {
        // Two rows, each under EfHatchWorkLogEntry.MaxReads on its own, with no
        // path shared between them - sixty distinct paths once folded. Counts
        // run downward from the path's own index, so the cap's cut line is
        // unambiguous: "049.cs" (count 11) survives, "050.cs" (count 10) does
        // not.
        var total = WorkLogRollup.MaxTopReads + 10;
        var all = Enumerable.Range(0, total).Select(i => ($"{i:D3}.cs", total - i)).ToArray();

        var reads = WorkLogRollup.FoldReads([
            Stored(all[..(total / 2)]),
            Stored(all[(total / 2)..]),
        ]);

        Assert.Equal(WorkLogRollup.MaxTopReads, reads.Count);
        Assert.Contains("049.cs", reads.Select(r => r.Path));
        Assert.DoesNotContain("050.cs", reads.Select(r => r.Path));
    }
}

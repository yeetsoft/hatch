namespace Hatch.Cli.Tests;

/// <summary>What one process learned about <c>/usage</c> on a CLI version, carried to the next.</summary>
public sealed class UsageProbeVerdictTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hatch-usage-verdict-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_verdict_round_trips_through_write_and_read()
    {
        var path = Path.Combine(_root, "verdict.json");
        var verdict = new UsageProbeVerdict("2.1.268 (Claude Code)", 0.38m);

        Assert.True(verdict.Write(path));

        var back = UsageProbeVerdict.Read(path);
        Assert.NotNull(back);
        Assert.Equal(verdict.Version, back.Version);
        Assert.Equal(verdict.CostUsd, back.CostUsd);
    }

    [Fact]
    public void A_verdict_with_no_cost_round_trips_too()
    {
        var path = Path.Combine(_root, "verdict.json");
        var verdict = new UsageProbeVerdict("2.1.268 (Claude Code)", null);

        Assert.True(verdict.Write(path));

        var back = UsageProbeVerdict.Read(path);
        Assert.NotNull(back);
        Assert.Null(back.CostUsd);
    }

    [Fact]
    public void A_verdict_written_where_no_directory_exists_yet_creates_it()
    {
        var path = Path.Combine(_root, "hatch", "nested", "usage-probe-verdict");
        var verdict = new UsageProbeVerdict("2.1.268 (Claude Code)", 0.38m);

        Assert.True(verdict.Write(path));

        var back = UsageProbeVerdict.Read(path);
        Assert.NotNull(back);
        Assert.Equal(verdict.Version, back.Version);
        Assert.Equal(verdict.CostUsd, back.CostUsd);
    }

    [Fact]
    public void A_verdict_whose_directory_cannot_be_created_answers_false_rather_than_throwing()
    {
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "a file where the directory should be");

        Assert.False(new UsageProbeVerdict("2.1.268 (Claude Code)", 0.38m).Write(Path.Combine(blocker, "verdict")));
    }

    [Fact]
    public void A_verdict_file_that_is_missing_or_broken_reads_as_no_verdict_rather_than_a_crash()
    {
        Assert.Null(UsageProbeVerdict.Read(Path.Combine(_root, "nothing.json")));
        Assert.Null(UsageProbeVerdict.Read(""));
        Assert.Null(UsageProbeVerdict.Read(null));

        var broken = Path.Combine(_root, "broken.json");
        File.WriteAllText(broken, "{ not json at all");
        Assert.Null(UsageProbeVerdict.Read(broken));

        var empty = Path.Combine(_root, "empty.json");
        File.WriteAllText(empty, "");
        Assert.Null(UsageProbeVerdict.Read(empty));
    }
}
